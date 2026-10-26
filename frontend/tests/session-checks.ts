import type { CenterTokens } from '../src/api/center-session.ts'

/**
 * 以重新装载模块模拟刷新页面，并检查续期、退出和嵌入身份隔离。
 * @param check 统一断言入口。
 * @returns 检查完成。
 */
export async function runSessionChecks(check: (name: string, value: boolean) => void): Promise<void> {
  const originalFetch = globalThis.fetch
  const originalWindow = Object.getOwnPropertyDescriptor(globalThis, 'window')
  const values = new Map<string, string>()
  const location = { pathname: '/' }
  const storage = {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value) },
    removeItem: (key: string) => { values.delete(key) },
  }
  Object.defineProperty(globalThis, 'window', { configurable: true, value: { sessionStorage: storage, location } })
  const adminKey = 'approval.admin.session.v1'
  const centerKey = 'approval.center.session.v1'
  const tokens: CenterTokens = { personId: 'p1', accessToken: 'old-access', accessExpiresAt: '2000-01-01T00:00:00Z',
    refreshToken: 'old-refresh', refreshExpiresAt: '2099-01-01T00:00:00Z', mustChangePassword: false }
  const rotated = { ...tokens, accessToken: 'new-access', refreshToken: 'new-refresh', accessExpiresAt: '2099-01-01T00:00:00Z' }
  let serial = 0
  /**
   * 用独立模块实例模拟页面刷新后的会话初始化。
   * @returns 新的管理会话模块。
   */
  async function admin(): Promise<typeof import('../src/api/admin-session.ts')> {
    return import('../src/api/admin-session.ts?session-check=' + ++serial)
  }
  /**
   * 重新装载审批中心模块，不复用上一个页面的内存变量。
   * @returns 新的中心会话模块。
   */
  async function center(): Promise<typeof import('../src/api/center-session.ts')> {
    return import('../src/api/center-session.ts?session-check=' + ++serial)
  }
  /**
   * 返回模拟身份接口的 JSON 响应。
   * @param body 响应数据。
   * @param status HTTP 状态。
   * @returns HTTP 响应。
   */
  function response(body: unknown, status = 200): Response {
    return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
  }
  try {
    globalThis.fetch = async () => response(tokens)
    const first = await admin()
    await first.loginAdmin('employee', 'never-store-this-password', 'reports')
    const restored = await admin()
    check('管理会话刷新后恢复身份和原范围，缓存不保存密码', restored.adminSession()?.tokens.personId === 'p1'
      && restored.adminSession()?.scope === 'reports' && !values.get(adminKey)?.includes('never-store-this-password'))
    let denied = false
    try { await restored.adminAccessToken('admin') } catch { denied = true }
    check('恢复报表会话不会扩大管理范围', denied)
    let calls = 0
    let release: (value: Response) => void = () => undefined
    globalThis.fetch = async () => { calls++; return new Promise<Response>(resolve => { release = resolve }) }
    const requests = [restored.adminAccessToken('reports'), restored.adminAccessToken('reports')]
    release(response(rotated))
    const result = await Promise.all(requests)
    check('管理并发续期只消费一次刷新令牌并保存轮换结果', calls === 1 && result.every(item => item === 'new-access')
      && JSON.parse(values.get(adminKey) ?? '{}').tokens.refreshToken === 'new-refresh')
    restored.logoutAdmin()
    check('管理退出后再次刷新仍为未登录', (await admin()).adminSession() === null && !values.has(adminKey))

    values.set(adminKey, '{broken')
    check('损坏管理缓存安全清除', (await admin()).adminSession() === null && !values.has(adminKey))
    values.set(adminKey, JSON.stringify({ employeeNumber: 'employee', scope: 'admin', tokens: { ...tokens, refreshExpiresAt: '2000-01-01' } }))
    check('过期缓存不恢复登录', (await admin()).adminSession() === null && !values.has(adminKey))

    globalThis.fetch = async () => response(tokens)
    const centerFirst = await center()
    await centerFirst.loginCenter('employee', 'never-store-this-password')
    const centerRestored = await center()
    check('审批中心普通登录刷新后恢复，缓存不保存密码', centerRestored.centerSession()?.personId === 'p1'
      && !values.get(centerKey)?.includes('never-store-this-password'))
    calls = 0
    globalThis.fetch = async () => { calls++; return new Promise<Response>(resolve => { release = resolve }) }
    const centerRequests = [centerRestored.centerAccessToken(), centerRestored.centerAccessToken()]
    release(response(rotated))
    await Promise.all(centerRequests)
    check('中心并发续期共享一次请求并保存新刷新令牌', calls === 1
      && JSON.parse(values.get(centerKey) ?? '{}').refreshToken === 'new-refresh')
    centerRestored.logoutCenter()
    check('中心退出后刷新不恢复登录', (await center()).centerSession() === null && !values.has(centerKey))

    values.set(adminKey, JSON.stringify({ employeeNumber: 'employee', scope: 'reports', tokens }))
    const interrupted = await admin()
    globalThis.fetch = async () => new Promise<Response>(resolve => { release = resolve })
    const pending = interrupted.adminAccessToken('reports').catch(() => 'discarded')
    interrupted.logoutAdmin()
    release(response(rotated))
    check('退出后的管理续期响应不能复活会话', await pending === 'discarded'
      && interrupted.adminSession() === null && !values.has(adminKey))

    values.set(centerKey, JSON.stringify(tokens))
    const revoked = await center()
    globalThis.fetch = async () => response({ code: 'UNAUTHORIZED', message: '登录已失效。' }, 401)
    try { await revoked.centerAccessToken() } catch { /* 预期失效会话被拒绝。 */ }
    check('服务端拒绝续期时清除中心缓存', revoked.centerSession() === null && !values.has(centerKey))

    values.set(adminKey, JSON.stringify({ employeeNumber: 'employee', scope: 'admin', tokens: rotated }))
    values.set(centerKey, JSON.stringify(rotated))
    location.pathname = '/embed/example'
    check('嵌入入口不继承普通中心或管理登录', (await admin()).adminSession() === null && (await center()).centerSession() === null)
    values.clear()
    const embedded = await center()
    globalThis.fetch = async () => response({ ...tokens, targetPath: '#/center/approvals/example' })
    await embedded.exchangeCenterEmbed('one-time-token')
    globalThis.fetch = async () => response(rotated)
    await embedded.centerAccessToken()
    check('嵌入兑换及续期始终只在内存中，刷新响应不丢失嵌入标记', !values.has(centerKey)
      && embedded.centerSession()?.targetPath === '#/center/approvals/example'
      && (await center()).centerSession() === null)

    location.pathname = '/'
    Object.defineProperty(window, 'sessionStorage', { get: () => { throw new Error('存储不可用') } })
    globalThis.fetch = async () => response(rotated)
    const privateMode = await center()
    await privateMode.loginCenter('employee', 'password')
    check('浏览器禁用存储时仍可内存登录并退出', await privateMode.centerAccessToken() === 'new-access')
    privateMode.logoutCenter()
  } finally {
    globalThis.fetch = originalFetch
    if (originalWindow) Object.defineProperty(globalThis, 'window', originalWindow)
    else Reflect.deleteProperty(globalThis, 'window')
  }
}
