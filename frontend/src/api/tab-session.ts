/**
 * 读取当前标签页的本地会话，存储禁用或缓存无效时回退为未登录。
 * @template T 会话类型。
 * @param key 本模块专用的存储键。
 * @param validate 会话结构和有效期检查。
 * @returns 有效会话或空值。
 */
export function readTabSession<T>(key: string, validate: (value: unknown) => value is T): T | null {
  try {
    if (typeof window === 'undefined') return null
    const text = window.sessionStorage.getItem(key)
    if (!text) return null
    const value: unknown = JSON.parse(text)
    if (validate(value)) return value
  } catch {
    // 浏览器隐私设置或损坏缓存不能阻止用户重新登录。
  }
  writeTabSession(key, null)
  return null
}

/**
 * 保存或清除当前标签页会话；浏览器禁止存储时保留内存登录能力。
 * @param key 本模块专用的存储键。
 * @param value 不含密码的会话；空值表示退出。
 */
export function writeTabSession(key: string, value: unknown | null): void {
  try {
    if (typeof window === 'undefined') return
    if (value === null) window.sessionStorage.removeItem(key)
    else window.sessionStorage.setItem(key, JSON.stringify(value))
  } catch {
    // 禁止持久存储的浏览器仍可在当前页面使用已建立的内存会话。
  }
}

/**
 * 确认已签发令牌可以在当前时间恢复，不延长服务端规定的期限。
 * @param accessExpiresAt 访问令牌到期时间。
 * @param refreshExpiresAt 刷新令牌到期时间。
 * @returns 时间格式有效且刷新令牌尚未过期时为真。
 */
export function canRestoreTokens(accessExpiresAt: string, refreshExpiresAt: string): boolean {
  return Number.isFinite(Date.parse(accessExpiresAt)) && Date.parse(refreshExpiresAt) > Date.now()
}

/**
 * 嵌入入口禁止恢复普通本地登录，避免一次性兑换失败后退回其他身份。
 * @returns 当前是否位于业务系统嵌入入口。
 */
export function isEmbedEntry(): boolean {
  return typeof window !== 'undefined' && window.location.pathname.startsWith('/embed/')
}
