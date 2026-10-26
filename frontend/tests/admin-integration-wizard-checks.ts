import { adminAccessToken, loginAdmin, logoutAdmin } from '../src/api/admin-session.ts'
import { getAppAdministrators, getWizardApps, queueTestCallback, saveAppAdministrators } from '../src/api/admin-integration-wizard.ts'

/**
 * 检查受限应用管理员与组织管理员的向导请求边界。
 * @param check 统一断言入口。
 * @returns 场景检查完成。
 */
export async function runAdminIntegrationWizardChecks(check: (name: string, value: boolean) => void): Promise<void> {
  const originalFetch = globalThis.fetch
  const visited: Array<{ url: string; method: string; body: string; authorization: string }> = []
  /**
   * 用固定响应记录页面是否误读组织级数据。
   * @param input 请求地址。
   * @param init 请求选项。
   * @returns 对应的模拟 HTTP 响应。
   */
  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const url = String(input)
    visited.push({ url, method: init?.method ?? 'GET', body: String(init?.body ?? ''),
      authorization: new Headers(init?.headers).get('Authorization') ?? '' })
    const body = url.endsWith('/identity/login') ? {
      personId: 'p1', accessToken: 'local-token', accessExpiresAt: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh-token', refreshExpiresAt: '2099-01-02T00:00:00Z', mustChangePassword: false,
    } : url.endsWith('/admin/session') ? { personId: 'p1' }
      : url.endsWith('/admin/apps') ? [{ code: 'known-app', name: '示例应用', isEnabled: true,
        allowedFlowCodes: ['采购、特殊'] }]
      : url.endsWith('/administrators') && init?.method === 'PUT' ? { code: 'known-app', personIds: ['p2'] }
        : url.endsWith('/administrators') ? { code: 'known-app', personIds: ['p1'] }
          : { eventId: 'test-123', status: 'queued' }
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  try {
    await loginAdmin('E1', 'password', 'app-test')
    let blockedAdmin = false
    try { await adminAccessToken() } catch { blockedAdmin = true }
    let blockedList = false
    try { await getAppAdministrators('known-app') } catch { blockedList = true }
    const receipt = await queueTestCallback('known-app', '验证验签')
    check('应用测试登录不读取组织管理员会话和应用列表，只能用已知编码排入测试回调',
      blockedAdmin && blockedList && receipt.eventId === 'test-123'
      && visited.length === 2 && visited[1]?.url.endsWith('/apps/known-app/test-callback')
      && visited[1]?.method === 'POST' && visited[1]?.authorization === 'Bearer local-token'
      && JSON.parse(visited[1]?.body ?? '{}').reason === '验证验签')
    logoutAdmin()
    visited.length = 0
    await loginAdmin('ADMIN', 'password', 'admin')
    const apps = await getWizardApps()
    const before = await getAppAdministrators('known-app')
    const after = await saveAppAdministrators('known-app', before.personIds, ['p2'], '调整负责人')
    check('组织管理员按旧名单条件更新应用管理员，且请求不携带凭据',
      apps[0]?.flowCodes[0] === '采购、特殊' && before.personIds.join() === 'p1' && after.personIds.join() === 'p2'
      && visited.some(item => item.url.endsWith('/administrators') && item.method === 'PUT'
        && JSON.parse(item.body).expectedPersonIds.join() === 'p1'
        && JSON.parse(item.body).reason === '调整负责人'
        && !/apiKey|signingSecret/.test(item.body)))
  } finally {
    logoutAdmin()
    globalThis.fetch = originalFetch
  }
}
