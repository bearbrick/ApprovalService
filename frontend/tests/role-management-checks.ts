import { loginAdmin, logoutAdmin } from '../src/api/admin-session.ts'
import { listManagedRoles, readManagedRole, saveManagedRole } from '../src/api/admin-role-management.ts'
import { ApiError } from '../src/api/http.ts'

/**
 * 验证角色请求使用本人 JWT、原始修订与显式原因，并保留并发错误。
 * @param check 检查记录器。
 * @returns 检查完成。
 */
export async function runRoleManagementChecks(check: (name: string, value: boolean) => void): Promise<void> {
  const originalFetch = globalThis.fetch
  const visited: Array<{ path: string; method: string; authorization: string; body: string }> = []
  let conflict = false
  let invalid = false
  /**
   * 记录经过通用请求层的角色交互。
   * @param input 请求路径。
   * @param init 请求选项。
   * @returns 受控 HTTP 响应。
   */
  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const path = String(input)
    visited.push({ path, method: init?.method ?? 'GET', authorization: new Headers(init?.headers).get('Authorization') ?? '', body: String(init?.body ?? '') })
    const role = { code: 'finance', name: '财务', revision: 7, isBuiltIn: false, personIds: ['p2'] }
    const body = path.endsWith('/identity/login') ? {
      personId: 'p1', accessToken: 'role-token', accessExpiresAt: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh', refreshExpiresAt: '2099-01-02T00:00:00Z', mustChangePassword: false,
    } : path.endsWith('/admin/session') ? { personId: 'p1' }
      : conflict ? { code: 'CONFLICT', message: '角色已变化，请刷新。', traceId: 'test' }
        : invalid ? [{ code: 'finance' }] : init?.method === 'PUT' ? role : [role]
    return new Response(JSON.stringify(body), { status: conflict ? 409 : 200, headers: { 'Content-Type': 'application/json' } })
  }
  try {
    await loginAdmin('admin', 'password')
    const roles = await listManagedRoles()
    await saveManagedRole('PUT', roles[0]!.code, true, { expectedRevision: roles[0]!.revision, personIds: ['p2'], reason: '调整成员' })
    const request = visited.at(-1)!
    check('角色成员提交携带本人 JWT、读取修订和审计原因', request.path.endsWith('/roles/finance/members')
      && request.authorization === 'Bearer role-token' && JSON.parse(request.body).expectedRevision === 7
      && JSON.parse(request.body).reason === '调整成员')
    conflict = true
    let rejected = false
    try { await saveManagedRole('PUT', 'finance', false, { name: '新名称', expectedRevision: 6, reason: '修改名称' }) }
    catch (cause) { rejected = cause instanceof ApiError && cause.status === 409 }
    check('角色并发冲突原样交给页面，不自动重试覆盖', rejected && visited.filter(item => item.path.endsWith('/roles/finance')).length === 1)
    conflict = false
    invalid = true
    let malformed = false
    try { await listManagedRoles() } catch { malformed = true }
    let duplicate = false
    try { readManagedRole({ code: 'x', name: 'x', revision: 0, isBuiltIn: false, personIds: ['p', 'p'] }) } catch { duplicate = true }
    check('角色列表缺少成员或成员重复时拒绝作为可编辑快照', malformed && duplicate)
  } finally {
    logoutAdmin()
    globalThis.fetch = originalFetch
  }
}
