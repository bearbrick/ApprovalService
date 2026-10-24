import { getJson, requestJson } from './http.ts'

/**
 * 本地身份接口返回的短期令牌；只放在当前页面内存。
 */
export interface AdminTokens {
  /**
   * 当前人员的内部 ID。
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
   * 逐次轮换的刷新令牌。
   */
  refreshToken: string
  /**
   * 刷新令牌到期时间。
   */
  refreshExpiresAt: string
  /**
   * 首次登录是否必须先修改密码。
   */
  mustChangePassword: boolean
}

/**
 * 当前页面内存中的管理员登录资料。
 */
export interface AdminSession {
  /**
   * 登录使用的工号，只用于改密后重新登录。
   */
  employeeNumber: string
  /**
   * 身份接口签发的令牌。
   */
  tokens: AdminTokens
  /**
   * 本次登录获准进入的管理范围；报表登录不能进入其他管理页面。
   */
  scope: 'admin' | 'reports' | 'flows'
}

/**
 * 当前页面内存中的会话，刷新页面即清除。
 */
let current: AdminSession | null = null

/**
 * 只接受身份接口完整的令牌结构。
 * @param value 服务端响应。
 * @returns 结构可用于登录时为真。
 */
function isTokens(value: unknown): value is AdminTokens {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const data = value as Record<string, unknown>
  return ['personId', 'accessToken', 'accessExpiresAt', 'refreshToken', 'refreshExpiresAt']
    .every(key => typeof data[key] === 'string' && (data[key] as string).length > 0)
    && typeof data.mustChangePassword === 'boolean'
}

/**
 * 使用工号和密码建立本地会话；管理范围登录核对组织管理员角色。
 * @param employeeNumber 本地工号。
 * @param password 本地密码。
 * @param scope 本次会话的页面范围。
 * @returns 登录结果；首次登录须先改密。
 */
export async function loginAdmin(employeeNumber: string, password: string,
  scope: 'admin' | 'reports' | 'flows' = 'admin'): Promise<AdminSession> {
  const tokens = await requestJson<unknown>('/api/v1/identity/login', {
    method: 'POST', body: { employeeNumber, password }, cache: 'no-store',
  })
  if (!isTokens(tokens)) throw new Error('登录响应不完整，请稍后重试。')
  current = { employeeNumber, tokens, scope }
  try {
    if (!tokens.mustChangePassword && scope === 'admin') await verifyAdminSession()
    if (!tokens.mustChangePassword && scope === 'flows') await verifyFlowManagerSession()
    return current
  } catch (error) {
    current = null
    throw error
  }
}

/**
 * 首次登录修改密码后重新签发令牌并再次核对管理角色。
 * @param currentPassword 当前密码。
 * @param newPassword 新密码。
 * @param scope 改密后的页面范围。
 * @returns 新会话。
 */
export async function changeAdminPassword(currentPassword: string, newPassword: string,
  scope: 'admin' | 'reports' | 'flows' = 'admin'): Promise<AdminSession> {
  if (!current) throw new Error('请先登录。')
  const employeeNumber = current.employeeNumber
  await requestJson<void>('/api/v1/identity/change-password', {
    method: 'POST', body: { currentPassword, newPassword },
    headers: { Authorization: `Bearer ${current.tokens.accessToken}` }, cache: 'no-store',
  })
  current = null
  return loginAdmin(employeeNumber, newPassword, scope)
}

/**
 * 页面范围守卫；报表登录只能停留在统计页。
 * @param session 当前本地会话。
 * @param section 管理后台一级模块。
 * @returns 是否允许进入该模块。
 */
export function canReadAdminSection(session: AdminSession | null, section: string): boolean {
  return !!session && (session.scope === 'admin' || session.scope === 'reports' && section === 'reports'
    || session.scope === 'flows' && section === 'flows')
}

/**
 * 读取当前页面会话。
 * @returns 会话或空值。
 */
export function adminSession(): AdminSession | null { return current }

/**
 * 注销并清除所有内存令牌。
 */
export function logoutAdmin(): void { current = null }

/**
 * 必要时轮换短期令牌，并限制报表会话不能调用其他管理接口。
 * @param requiredScope 调用方所需的页面范围。
 * @returns 可使用的访问令牌。
 */
export async function adminAccessToken(requiredScope: 'admin' | 'reports' | 'flows' = 'admin'): Promise<string> {
  if (!current || current.tokens.mustChangePassword) throw new Error('请先完成管理员登录。')
  if (current.scope === 'reports' && requiredScope !== 'reports') throw new Error('此会话仅可查看报表。')
  if (current.scope === 'flows' && requiredScope !== 'flows') throw new Error('此会话仅可管理流程。')
  if (Date.parse(current.tokens.accessExpiresAt) > Date.now() + 30_000) return current.tokens.accessToken
  const tokens = await requestJson<unknown>('/api/v1/identity/refresh', {
    method: 'POST', body: { refreshToken: current.tokens.refreshToken }, cache: 'no-store',
  })
  if (!isTokens(tokens) || tokens.mustChangePassword || tokens.personId !== current.tokens.personId) {
    current = null
    throw new Error('登录续期失败，请重新登录。')
  }
  current = { ...current, tokens }
  return tokens.accessToken
}

/**
 * 通过受保护端点核对当前本地会话仍有组织管理员权限。
 * @returns 已核对的内部人员 ID。
 */
export async function verifyAdminSession(): Promise<string> {
  if (!current) throw new Error('请先登录。')
  const result = await getJson<{ personId: string }>('/api/v1/admin/session', {
    headers: { Authorization: `Bearer ${current.tokens.accessToken}` }, cache: 'no-store',
  })
  if (!result || result.personId !== current.tokens.personId) throw new Error('管理员身份核验失败，请重新登录。')
  return result.personId
}

/**
 * 用受保护的流程目录核对当前本地会话有直接部门管理权限。
 * @returns 已核对的内部人员 ID。
 */
export async function verifyFlowManagerSession(): Promise<string> {
  if (!current) throw new Error('请先登录。')
  const result = await getJson<unknown>('/api/v1/admin/flows', {
    headers: { Authorization: `Bearer ${current.tokens.accessToken}` }, cache: 'no-store',
  })
  if (!Array.isArray(result)) throw new Error('流程管理权限核验失败，请重新登录。')
  return current.tokens.personId
}
