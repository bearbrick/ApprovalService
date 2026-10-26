import { ApiError, requestJson } from './http.ts'
import { canRestoreTokens, isEmbedEntry, readTabSession, writeTabSession } from './tab-session.ts'

/**
 * 身份服务签发的令牌；普通登录与嵌入登录使用相同响应结构。
 */
export interface IdentityTokens {
  /**
   * 当前人员内部标识。
   */
  personId: string
  /**
   * 短期访问令牌。
   */
  accessToken: string
  /**
   * 访问令牌有效期。
   */
  accessExpiresAt: string
  /**
   * 单次使用的刷新令牌。
   */
  refreshToken: string
  /**
   * 刷新令牌有效期。
   */
  refreshExpiresAt: string
  /**
   * 是否必须先修改临时密码。
   */
  mustChangePassword: boolean
  /**
   * 嵌入兑换指定的页面，普通登录不得携带。
   */
  targetPath?: string | null
}

/**
 * 同一标签页各工作区共用的普通身份，不记录密码或授权结论。
 */
export interface LocalIdentity {
  /**
   * 登录工号，用于改密后重新登录。
   */
  employeeNumber: string
  /**
   * 当前签发的令牌。
   */
  tokens: IdentityTokens
}

/**
 * 普通会话唯一缓存键，避免两个模块消费同一刷新令牌。
 */
const storageKey = 'approval.local.session.v1'

/**
 * 核对身份响应的必要字段。
 * @param value 未知响应。
 * @returns 令牌结构完整时为真。
 */
export function isIdentityTokens(value: unknown): value is IdentityTokens {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const data = value as Record<string, unknown>
  return ['personId', 'accessToken', 'accessExpiresAt', 'refreshToken', 'refreshExpiresAt']
    .every(key => typeof data[key] === 'string' && (data[key] as string).length > 0)
    && typeof data.mustChangePassword === 'boolean'
}

/**
 * 只恢复完整、未过期且已完成改密的普通身份。
 * @param value 缓存内容。
 * @returns 是否可恢复。
 */
function isStoredIdentity(value: unknown): value is LocalIdentity {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const data = value as LocalIdentity
  return typeof data.employeeNumber === 'string' && isIdentityTokens(data.tokens)
    && !data.tokens.targetPath && !data.tokens.mustChangePassword
    && canRestoreTokens(data.tokens.accessExpiresAt, data.tokens.refreshExpiresAt)
}

/**
 * 删除迁移前的独立缓存，避免退出后又恢复旧身份。
 */
function clearLegacy(): void {
  writeTabSession('approval.admin.session.v1', null)
  writeTabSession('approval.center.session.v1', null)
}

/**
 * 恢复统一会话，首次升级优先迁移原管理身份，再迁移中心身份。
 * @returns 可恢复的普通身份。
 */
function restore(): LocalIdentity | null {
  if (isEmbedEntry()) return null
  let identity = readTabSession(storageKey, isStoredIdentity)
  if (!identity) identity = readTabSession('approval.admin.session.v1', isStoredIdentity)
  if (!identity) {
    const tokens = readTabSession('approval.center.session.v1', (value): value is IdentityTokens =>
      isStoredIdentity({ employeeNumber: '', tokens: value }))
    if (tokens) identity = { employeeNumber: '', tokens }
  }
  if (identity) writeTabSession(storageKey, { employeeNumber: identity.employeeNumber, tokens: identity.tokens })
  clearLegacy()
  return identity
}

/**
 * 创建普通登录状态容器；生产代码只使用下方单例，检查可创建新容器模拟刷新。
 * @returns 共用的登录、读取、退出和续期操作。
 */
export function createLocalSession() {
  let current = restore()
  let refreshing: Promise<string> | null = null
  let revision = 0

  /**
   * 读取当前普通身份；嵌入页面不能继承。
   * @returns 普通身份或空值。
   */
  function session(): LocalIdentity | null { return isEmbedEntry() ? null : current }

  /**
   * 退出所有普通工作区，废弃迟到的登录与续期响应。
   */
  function logout(): void {
    revision++
    current = null
    refreshing = null
    if (!isEmbedEntry()) { writeTabSession(storageKey, null); clearLegacy() }
  }

  /**
   * 使用工号密码建立普通身份；权限由各接口实时检查。
   * @param employeeNumber 工号。
   * @param password 用户输入的密码，只用于本次请求。
   * @returns 新的普通身份。
   */
  async function login(employeeNumber: string, password: string): Promise<LocalIdentity> {
    if (isEmbedEntry()) throw new Error('请在独立审批服务页面登录。')
    logout()
    const attempt = revision
    const tokens = await requestJson<unknown>('/api/v1/identity/login', {
      method: 'POST', body: { employeeNumber, password }, cache: 'no-store',
    })
    if (revision !== attempt) throw new Error('登录状态已改变，请重新进入页面。')
    if (!isIdentityTokens(tokens) || tokens.targetPath
      || !canRestoreTokens(tokens.accessExpiresAt, tokens.refreshExpiresAt)) throw new Error('登录响应不完整，请稍后重试。')
    current = { employeeNumber, tokens }
    writeTabSession(storageKey, tokens.mustChangePassword ? null : current)
    return current
  }

  /**
   * 轮换同一身份的令牌，网络故障保留身份供用户重试。
   * @param identity 发起续期时的身份快照。
   * @returns 新的访问令牌。
   */
  async function rotate(identity: LocalIdentity): Promise<string> {
    try {
      const tokens = await requestJson<unknown>('/api/v1/identity/refresh', {
        method: 'POST', body: { refreshToken: identity.tokens.refreshToken }, cache: 'no-store',
      })
      if (current !== identity) throw new Error('登录状态已改变，请重新进入页面。')
      if (!isIdentityTokens(tokens) || tokens.targetPath || tokens.mustChangePassword
        || tokens.personId !== identity.tokens.personId
        || !canRestoreTokens(tokens.accessExpiresAt, tokens.refreshExpiresAt)) {
        logout()
        throw new Error('登录续期失败，请重新登录。')
      }
      current = { ...identity, tokens }
      writeTabSession(storageKey, current)
      return tokens.accessToken
    } catch (error) {
      if (current === identity && error instanceof ApiError && [400, 401, 403].includes(error.status)) logout()
      throw error
    }
  }

  /**
   * 所有普通工作区共用单次续期，在请求业务接口前取得有效令牌。
   * @returns 有效访问令牌。
   */
  async function accessToken(): Promise<string> {
    const identity = session()
    if (!identity || identity.tokens.mustChangePassword) throw new Error('请先完成登录和首次改密。')
    if (!canRestoreTokens(identity.tokens.accessExpiresAt, identity.tokens.refreshExpiresAt)) {
      logout()
      throw new Error('登录已过期，请重新登录。')
    }
    if (Date.parse(identity.tokens.accessExpiresAt) > Date.now() + 30_000) return identity.tokens.accessToken
    if (!refreshing) {
      const operation = rotate(identity).finally(() => { if (refreshing === operation) refreshing = null })
      refreshing = operation
    }
    return refreshing
  }

  return { session, login, logout, accessToken }
}

/**
 * 审批中心、管理后台和设计器共享的唯一普通会话。
 */
export const localSession = createLocalSession()
