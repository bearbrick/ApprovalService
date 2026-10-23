import { requestJson } from './http.ts'

/**
 * 登录和嵌入兑换的令牌响应，只在当前页面内存中保存。
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
 * 当前页面内存中的可信登录响应。
 */

let current: CenterTokens | null = null

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
 * 读取当前页面会话，不跨标签或重启浏览器持久保存令牌。
 * @returns 当前会话；未登录时为空。
 */
export function centerSession(): CenterTokens | null { return current }

/**
 * 注销当前页面会话。
 */
export function logoutCenter(): void { current = null }

/**
 * 在令牌临近过期时轮换访问与刷新令牌。
 * @returns 可用于请求的访问令牌。
 */
export async function centerAccessToken(): Promise<string> {
  if (!current) throw new Error('请先登录审批中心。')
  if (Date.parse(current.accessExpiresAt) > Date.now() + 30_000) return current.accessToken
  const result = await requestJson<CenterTokens>('/api/v1/identity/refresh', {
    method: 'POST', body: { refreshToken: current.refreshToken }, cache: 'no-store',
  })
  if (!isTokens(result)) throw new Error('登录续期响应不完整，请重新登录。')
  current = result
  return result.accessToken
}
