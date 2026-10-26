import { ApiError, requestJson } from './http.ts'
import { canRestoreTokens, isEmbedEntry, readTabSession, writeTabSession } from './tab-session.ts'

/**
 * 登录和嵌入兑换的令牌响应；嵌入会话仅在当前页面内存保存。
 */
export interface CenterTokens {
  /**
   * 当前登录人员的内部标识。
   */

  personId: string
  /**
   * 短期访问令牌。
   */

  accessToken: string
  /**
   * 访问令牌到期时间。
   */

  accessExpiresAt: string
  /**
   * 可轮换的刷新令牌。
   */

  refreshToken: string
  /**
   * 刷新令牌到期时间。
   */

  refreshExpiresAt: string
  /**
   * 首次登录是否需要先改密。
   */

  mustChangePassword: boolean
  /**
   * 嵌入会话允许打开的相对页面。
   */

  targetPath?: string | null
}

/**
 * 普通本地登录的标签页缓存键。
 */
const storageKey = 'approval.center.session.v1'

/**
 * 当前会话；业务系统嵌入入口必须重新兑换，不能恢复普通本地身份。
 */
let current: CenterTokens | null = isEmbedEntry() ? null : readTabSession(storageKey, isStoredSession)

/**
 * 同一会话共享进行中的刷新请求，防止重复消费刷新令牌。
 */
let refreshing: Promise<string> | null = null

/**
 * 只恢复尚未过期、已完成首次改密的普通本地会话。
 * @param value 缓存的候选值。
 * @returns 是否可以恢复。
 */
function isStoredSession(value: unknown): value is CenterTokens {
  return isTokens(value) && !value.targetPath && !value.mustChangePassword
    && canRestoreTokens(value.accessExpiresAt, value.refreshExpiresAt)
}

/**
 * 校验身份接口返回的必要字段，避免残缺令牌进入请求头。
 * @param value 待校验响应。
 * @returns 是否为完整令牌响应。
 */
function isTokens(value: unknown): value is CenterTokens {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const data = value as Record<string, unknown>
  return ['personId', 'accessToken', 'accessExpiresAt', 'refreshToken', 'refreshExpiresAt']
    .every(key => typeof data[key] === 'string' && (data[key] as string).length > 0)
    && typeof data.mustChangePassword === 'boolean'
}

/**
 * 使用本地工号密码登录。
 * @param employeeNumber 工号。
 * @param password 密码。
 * @returns 登录结果。
 */
export async function loginCenter(employeeNumber: string, password: string): Promise<CenterTokens> {
  const result = await requestJson<CenterTokens>('/api/v1/identity/login', {
    method: 'POST', body: { employeeNumber, password }, cache: 'no-store',
  })
  if (!isTokens(result)) throw new Error('登录响应不完整，请稍后重试。')
  current = result
  writeTabSession(storageKey, result.mustChangePassword ? null : result)
  return result
}

/**
 * 将一次性嵌入令牌兑换为当前页面会话。
 * @param token 业务系统传入的一次性令牌。
 * @returns 兑换结果。
 */
export async function exchangeCenterEmbed(token: string): Promise<CenterTokens> {
  const result = await requestJson<CenterTokens>('/api/v1/embed-tokens/exchange', {
    method: 'POST', body: { token }, cache: 'no-store',
  })
  if (!isTokens(result)) throw new Error('嵌入登录响应不完整，请稍后重试。')
  current = result
  return result
}

/**
 * 读取当前页面会话，普通本地登录可从当前标签页恢复。
 * @returns 当前会话；未登录时为空。
 */
export function centerSession(): CenterTokens | null { return current }

/**
 * 注销当前页面会话并移除普通本地登录的标签页缓存。
 */
export function logoutCenter(): void {
  current = null
  refreshing = null
  writeTabSession(storageKey, null)
}

/**
 * 在令牌临近过期时轮换访问与刷新令牌。
 * @returns 可用于请求的访问令牌。
 */
export async function centerAccessToken(): Promise<string> {
  if (!current) throw new Error('请先登录审批中心。')
  if (!canRestoreTokens(current.accessExpiresAt, current.refreshExpiresAt)) {
    logoutCenter()
    throw new Error('登录已过期，请重新登录。')
  }
  if (Date.parse(current.accessExpiresAt) > Date.now() + 30_000) return current.accessToken
  if (!refreshing) {
    const operation = rotateCenterSession(current).finally(() => {
      if (refreshing === operation) refreshing = null
    })
    refreshing = operation
  }
  return refreshing
}

/**
 * 续期并保留本地或嵌入身份边界，退出后的迟到结果不得恢复登录。
 * @param session 发起续期时的会话。
 * @returns 续期后的访问令牌。
 */
async function rotateCenterSession(session: CenterTokens): Promise<string> {
  try {
    const result = await requestJson<CenterTokens>('/api/v1/identity/refresh', {
      method: 'POST', body: { refreshToken: session.refreshToken }, cache: 'no-store',
    })
    if (current !== session) throw new Error('登录状态已改变，请重新进入页面。')
    if (!isTokens(result) || result.personId !== session.personId
      || (result.targetPath != null && result.targetPath !== session.targetPath)
      || !canRestoreTokens(result.accessExpiresAt, result.refreshExpiresAt)) {
      logoutCenter()
      throw new Error('登录续期响应不完整，请重新登录。')
    }
    // 刷新接口保留嵌入 JWT 的应用身份，但不重复返回首次兑换的页面路径。
    current = { ...result, targetPath: session.targetPath ?? null }
    if (!current.targetPath && !current.mustChangePassword) writeTabSession(storageKey, current)
    return result.accessToken
  } catch (error) {
    if (current === session && error instanceof ApiError && [400, 401, 403].includes(error.status)) logoutCenter()
    throw error
  }
}
