import { ApiError, requestJson } from './http.ts'
import { isIdentityTokens, localSession } from './local-session.ts'
import type { IdentityTokens } from './local-session.ts'
import { canRestoreTokens, isEmbedEntry } from './tab-session.ts'

/**
 * 中心登录响应与统一身份结构一致。
 */
export type CenterTokens = IdentityTokens

/**
 * 嵌入身份始终只存内存，不能写入或恢复普通登录缓存。
 */
let embedded: CenterTokens | null = null
/**
 * 一旦进入嵌入兑换流程，失败后也不能回退到普通身份。
 */
let embedMode = isEmbedEntry()
/**
 * 嵌入会话的单次续期，与普通身份的续期相互隔离。
 */
let refreshing: Promise<string> | null = null
/**
 * 退出或重新兑换时废弃先前的迟到响应。
 */
let generation = 0

/**
 * 建立各普通工作区共用的本地登录。
 * @param employeeNumber 工号。
 * @param password 密码。
 * @returns 当前令牌。
 */
export async function loginCenter(employeeNumber: string, password: string): Promise<CenterTokens> {
  const identity = await localSession.login(employeeNumber, password)
  generation++
  embedded = null
  refreshing = null
  embedMode = false
  return identity.tokens
}

/**
 * 兑换独立的嵌入身份，不能覆盖普通登录。
 * @param token 业务系统提供的一次性令牌。
 * @returns 嵌入令牌。
 */
export async function exchangeCenterEmbed(token: string): Promise<CenterTokens> {
  embedMode = true
  embedded = null
  refreshing = null
  const attempt = ++generation
  const result = await requestJson<unknown>('/api/v1/embed-tokens/exchange', {
    method: 'POST', body: { token }, cache: 'no-store',
  })
  if (generation !== attempt) throw new Error('嵌入登录状态已改变，请重新打开。')
  if (!isIdentityTokens(result) || !result.targetPath) throw new Error('嵌入登录响应不完整，请稍后重试。')
  embedded = result
  return result
}

/**
 * 普通中心与管理后台共用身份，嵌入入口只读取本次兑换结果。
 * @returns 当前令牌或空值。
 */
export function centerSession(): CenterTokens | null {
  return embedMode || isEmbedEntry() ? embedded : localSession.session()?.tokens ?? null
}

/**
 * 普通退出影响全部工作区；嵌入退出仅清理本次嵌入身份。
 */
export function logoutCenter(): void {
  generation++
  embedded = null
  refreshing = null
  if (!embedMode && !isEmbedEntry()) localSession.logout()
}

/**
 * 取得中心令牌，普通身份共享单次续期，嵌入身份单独轮换。
 * @returns 可用于请求的访问令牌。
 */
export async function centerAccessToken(): Promise<string> {
  if (!embedMode && !isEmbedEntry()) return localSession.accessToken()
  const session = embedded
  if (!session) throw new Error('请从业务系统重新打开审批页面。')
  if (!canRestoreTokens(session.accessExpiresAt, session.refreshExpiresAt)) {
    logoutCenter()
    throw new Error('嵌入登录已过期，请从业务系统重新打开。')
  }
  if (Date.parse(session.accessExpiresAt) > Date.now() + 30_000) return session.accessToken
  if (!refreshing) {
    const operation = rotateEmbedSession(session).finally(() => { if (refreshing === operation) refreshing = null })
    refreshing = operation
  }
  return refreshing
}

/**
 * 独立轮换嵌入令牌，保留目标路径并拒绝迟到或换人响应。
 * @param session 发起续期的嵌入身份。
 * @returns 新访问令牌。
 */
async function rotateEmbedSession(session: CenterTokens): Promise<string> {
  try {
    const result = await requestJson<unknown>('/api/v1/identity/refresh', {
      method: 'POST', body: { refreshToken: session.refreshToken }, cache: 'no-store',
    })
    if (embedded !== session) throw new Error('嵌入登录状态已改变，请重新打开。')
    if (!isIdentityTokens(result) || result.mustChangePassword || result.personId !== session.personId
      || (result.targetPath != null && result.targetPath !== session.targetPath)
      || !canRestoreTokens(result.accessExpiresAt, result.refreshExpiresAt)) {
      logoutCenter()
      throw new Error('嵌入续期响应不完整，请重新打开。')
    }
    embedded = { ...result, targetPath: session.targetPath }
    return result.accessToken
  } catch (error) {
    if (embedded === session && error instanceof ApiError && [400, 401, 403].includes(error.status)) logoutCenter()
    throw error
  }
}
