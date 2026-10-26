import { getJson, requestJson } from './http.ts'
import { localSession } from './local-session.ts'
import { loginCenter } from './center-session.ts'
import type { IdentityTokens } from './local-session.ts'

/**
 * 管理接口沿用统一普通身份令牌。
 */
export type AdminTokens = IdentityTokens

/**
 * 管理页面可读取的普通登录资料，不保存模块授权结论。
 */
export interface AdminSession {
  /**
   * 登录工号，只用于改密后重新登录。
   */
  employeeNumber: string
  /**
   * 与审批中心共用的令牌。
   */
  tokens: AdminTokens
}

/**
 * 兼容现有调用方的模块提示，不能作为授权依据。
 */
type AdminScope = 'admin' | 'reports' | 'flows' | 'app-test'

/**
 * 建立普通登录，进入不同管理模块时由服务端核验实际权限。
 * @param employeeNumber 工号。
 * @param password 密码。
 * @param _scope 调用方模块提示；不再限制或授予身份权限。
 * @returns 普通登录资料。
 */
export async function loginAdmin(employeeNumber: string, password: string,
  _scope: AdminScope = 'admin'): Promise<AdminSession> {
  await loginCenter(employeeNumber, password)
  return localSession.session()!
}

/**
 * 修改密码后使用同一工号重建所有普通工作区的登录。
 * @param currentPassword 当前密码。
 * @param newPassword 新密码。
 * @param scope 调用方模块提示。
 * @returns 新会话。
 */
export async function changeAdminPassword(currentPassword: string, newPassword: string,
  scope: AdminScope = 'admin'): Promise<AdminSession> {
  const identity = localSession.session()
  if (!identity) throw new Error('请先登录。')
  if (!identity.employeeNumber) throw new Error('请重新登录一次后再修改密码。')
  // 首次改密令牌只允许改密；普通改密先续期，避免编辑期间过期。
  const token = identity.tokens.mustChangePassword ? identity.tokens.accessToken : await localSession.accessToken()
  await requestJson<void>('/api/v1/identity/change-password', {
    method: 'POST', body: { currentPassword, newPassword },
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  logoutAdmin()
  return loginAdmin(identity.employeeNumber, newPassword, scope)
}

/**
 * 管理页面登录守卫；模块授权由页面调用的受保护接口决定。
 * @param session 当前普通身份。
 * @param _section 当前模块，不根据登录入口推断权限。
 * @returns 是否已完成普通登录。
 */
export function canReadAdminSection(session: AdminSession | null, _section: string): boolean {
  return !!session && !session.tokens.mustChangePassword
}

/**
 * 读取普通身份，审批中心登录后可直接进入管理页面核验权限。
 * @returns 当前身份。
 */
export function adminSession(): AdminSession | null { return localSession.session() }

/**
 * 退出当前标签页的所有普通工作区。
 */
export function logoutAdmin(): void { localSession.logout() }

/**
 * 取得统一令牌；接口自身核对角色和资源范围，不要求重复登录。
 * @param _requiredScope 调用方模块提示，不作为授权结论。
 * @returns 已续期的访问令牌。
 */
export async function adminAccessToken(_requiredScope: AdminScope = 'admin'): Promise<string> {
  return localSession.accessToken()
}

/**
 * 用受保护端点验证组织管理员资格，拒绝访问不清除普通身份。
 * @returns 已核对的内部人员 ID。
 */
export async function verifyAdminSession(): Promise<string> {
  const token = await adminAccessToken()
  const result = await getJson<{ personId: string }>('/api/v1/admin/session', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  if (!result || result.personId !== adminSession()?.tokens.personId) throw new Error('管理员身份核验失败，请重新进入页面。')
  return result.personId
}

/**
 * 用受保护目录核验流程管理权限，拒绝访问不清除普通身份。
 * @returns 已核对的内部人员 ID。
 */
export async function verifyFlowManagerSession(): Promise<string> {
  const token = await adminAccessToken('flows')
  const result = await getJson<unknown>('/api/v1/admin/flows', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  if (!Array.isArray(result)) throw new Error('流程管理权限核验失败，请重新进入页面。')
  return adminSession()!.tokens.personId
}
