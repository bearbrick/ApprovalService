import { ApiError } from '../src/api/http.ts'
import { createLocalSession, localSession } from '../src/api/local-session.ts'
import type { IdentityTokens } from '../src/api/local-session.ts'
import { adminAccessToken, adminSession, loginAdmin, logoutAdmin, verifyAdminSession } from '../src/api/admin-session.ts'
import { centerAccessToken, centerSession, loginCenter, logoutCenter } from '../src/api/center-session.ts'

/**
 * 检查普通身份跨工作区共享、刷新恢复、单次续期与嵌入隔离。
 * @param check 统一场景断言。
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
  const key = 'approval.local.session.v1'
  const tokens: IdentityTokens = { personId: 'p1', accessToken: 'old-access', accessExpiresAt: '2000-01-01T00:00:00Z',
    refreshToken: 'old-refresh', refreshExpiresAt: '2099-01-01T00:00:00Z', mustChangePassword: false }
  const rotated = { ...tokens, accessToken: 'new-access', refreshToken: 'new-refresh', accessExpiresAt: '2099-01-01T00:00:00Z' }
  let release: (response: Response) => void = () => undefined
  try {
    globalThis.fetch = async () => Response.json(tokens)
    await loginAdmin('employee', 'never-store-this-password', 'flows')
    check('流程入口登录后审批中心直接使用同一身份，无需第二次登录',
      centerSession() === adminSession()?.tokens && centerSession()?.personId === 'p1')
    const restored = createLocalSession()
    check('刷新后恢复统一身份且不保存密码或前端权限结论', restored.session()?.employeeNumber === 'employee'
      && restored.session()?.tokens.personId === 'p1' && !values.get(key)?.includes('never-store-this-password')
      && !values.get(key)?.includes('scope'))
    let calls = 0
    globalThis.fetch = async () => { calls++; return new Promise<Response>(resolve => { release = resolve }) }
    const requests = [adminAccessToken('flows'), centerAccessToken(), adminAccessToken('reports')]
    release(Response.json(rotated))
    const results = await Promise.all(requests)
    check('中心与多个管理模块同时续期只消费一次刷新令牌', calls === 1
      && results.every(item => item === 'new-access') && centerSession() === adminSession()?.tokens
      && createLocalSession().session()?.tokens.refreshToken === 'new-refresh')

    globalThis.fetch = async () => Response.json({ code: 'FORBIDDEN', message: '没有组织管理权限。' }, { status: 403 })
    let forbidden = false
    try { await verifyAdminSession() } catch (cause) { forbidden = cause instanceof ApiError && cause.status === 403 }
    check('权限核验 403 不注销普通身份，仍可使用审批中心令牌', forbidden
      && await centerAccessToken() === 'new-access' && values.has(key))
    logoutAdmin()
    check('在管理后台退出同时清除中心和刷新恢复身份', centerSession() === null && createLocalSession().session() === null)

    globalThis.fetch = async () => Response.json(rotated)
    await loginCenter('employee', 'password')
    check('中心登录后管理后台与设计器直接读取同一身份', adminSession()?.tokens === centerSession()
      && await adminAccessToken('admin') === 'new-access')
    logoutCenter()
    check('中心退出同时清除管理身份', adminSession() === null && !values.has(key))

    values.set(key, '{broken')
    check('损坏缓存清除后保持未登录', createLocalSession().session() === null && !values.has(key))
    values.set(key, JSON.stringify({ employeeNumber: 'employee', tokens: { ...tokens, refreshExpiresAt: '2000-01-01' } }))
    check('刷新令牌已过期不恢复登录', createLocalSession().session() === null && !values.has(key))
    values.set('approval.admin.session.v1', JSON.stringify({ employeeNumber: 'employee', scope: 'flows', tokens: rotated }))
    values.set('approval.center.session.v1', JSON.stringify({ ...rotated, personId: 'old-center-person' }))
    const migrated = createLocalSession()
    check('旧版两份缓存只迁移一份普通身份并清除旧键', migrated.session()?.tokens.personId === 'p1'
      && !values.has('approval.admin.session.v1') && !values.has('approval.center.session.v1'))
    migrated.logout()
    check('迁移后退出不会复活旧中心或管理身份', createLocalSession().session() === null)

    const unstable = createLocalSession()
    globalThis.fetch = async () => Response.json(tokens)
    await unstable.login('employee', 'password')
    globalThis.fetch = async () => { throw new Error('临时网络故障') }
    try { await unstable.accessToken() } catch { /* 预期保留身份用于重试。 */ }
    check('临时网络失败保留身份和缓存', unstable.session()?.tokens.refreshToken === 'old-refresh' && values.has(key))
    globalThis.fetch = async () => Response.json(rotated)
    check('网络恢复后无需密码即可续期', await unstable.accessToken() === 'new-access')

    globalThis.fetch = async () => Response.json(tokens)
    await unstable.login('employee', 'password')
    globalThis.fetch = async () => new Promise<Response>(resolve => { release = resolve })
    const pending = unstable.accessToken().catch(() => 'discarded')
    unstable.logout()
    release(Response.json(rotated))
    check('退出后的迟到续期不能复活身份', await pending === 'discarded' && unstable.session() === null && !values.has(key))
    const pendingLogin = unstable.login('employee', 'password').catch(() => null)
    unstable.logout()
    release(Response.json(rotated))
    check('退出后的迟到登录也不能复活身份', await pendingLogin === null && unstable.session() === null)

    globalThis.fetch = async () => Response.json(tokens)
    await unstable.login('employee', 'password')
    globalThis.fetch = async () => Response.json({ code: 'UNAUTHORIZED', message: '刷新身份已失效。' }, { status: 401 })
    try { await unstable.accessToken() } catch { /* 服务端撤销后确实需要重新登录。 */ }
    check('服务端撤销刷新身份后清除统一缓存', unstable.session() === null && !values.has(key))

    globalThis.fetch = async () => Response.json({ ...rotated, mustChangePassword: true })
    await unstable.login('employee', 'temporary-password')
    let mustChangeDenied = false
    try { await unstable.accessToken() } catch { mustChangeDenied = true }
    check('首次改密身份不缓存且不能获取普通业务令牌', mustChangeDenied && !values.has(key))
    unstable.logout()

    globalThis.fetch = async () => Response.json(rotated)
    await loginCenter('employee', 'password')
    const ordinaryCache = values.get(key)
    location.pathname = '/embed/example'
    const embedded = await import('../src/api/center-session.ts?' + 'unified-embed-check')
    check('嵌入入口不继承普通身份，管理入口也不可用', embedded.centerSession() === null && adminSession() === null)
    globalThis.fetch = async () => Response.json({ ...tokens, personId: 'embed-person', targetPath: '#/center/approvals/example' })
    await embedded.exchangeCenterEmbed('one-time-token')
    globalThis.fetch = async () => Response.json({ ...rotated, personId: 'embed-person' })
    await embedded.centerAccessToken()
    check('嵌入续期保持目标和人员且不污染普通缓存', embedded.centerSession()?.personId === 'embed-person'
      && embedded.centerSession()?.targetPath === '#/center/approvals/example' && values.get(key) === ordinaryCache)
    embedded.logoutCenter()
    check('嵌入退出不删除普通缓存且不能回退继承', embedded.centerSession() === null && values.get(key) === ordinaryCache)
    location.pathname = '/'
    check('回到普通页面仍使用原普通身份', adminSession()?.tokens.personId === 'p1')
    logoutCenter()

    Object.defineProperty(window, 'sessionStorage', { get: () => { throw new Error('存储不可用') } })
    globalThis.fetch = async () => Response.json(rotated)
    await loginAdmin('employee', 'password')
    check('禁用浏览器存储时所有普通工作区仍共享内存身份', await centerAccessToken() === 'new-access')
    logoutCenter()
  } finally {
    location.pathname = '/'
    localSession.logout()
    globalThis.fetch = originalFetch
    if (originalWindow) Object.defineProperty(globalThis, 'window', originalWindow)
    else Reflect.deleteProperty(globalThis, 'window')
  }
}
