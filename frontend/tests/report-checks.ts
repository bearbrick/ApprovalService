import { ApiError } from '../src/api/http.ts'
import { adminAccessToken, canReadAdminSection, loginAdmin, logoutAdmin } from '../src/api/admin-session.ts'
import { loadAdminReports, reportQuery } from '../src/api/admin-reports.ts'
import type { ReportFilter } from '../src/api/admin-reports.ts'
import { adminAdapter } from '../src/api/admin-adapter.ts'

/**
 * 在无浏览器条件下验证报表筛选、JWT 范围及四条 HTTP 响应。
 * @param check 统一场景断言入口。
 * @returns 场景完成。
 */
export async function runReportChecks(check: (name: string, value: boolean) => void): Promise<void> {
  const filter: ReportFilter = {
    fromLocal: '2026-10-01T09:00', untilLocal: '2026-10-02T09:00',
    flowCode: ' PUBLIC ', clientAppCode: ' ERP ', departmentId: ' D1 ',
  }
  const query = new URLSearchParams(reportQuery(filter).slice(1))
  check('报表本地时间换成带 Z 的 UTC 半开区间并保留维度筛选',
    query.get('fromUtc') === new Date(filter.fromLocal).toISOString() &&
    query.get('untilUtc') === new Date(filter.untilLocal).toISOString() &&
    query.get('flowCode') === 'PUBLIC' && query.get('clientAppCode') === 'ERP' && query.get('departmentId') === 'D1')
  let rejectedRange = false
  try { reportQuery({ ...filter, untilLocal: filter.fromLocal }) } catch { rejectedRange = true }
  check('报表拒绝空时间段', rejectedRange)

  const originalFetch = globalThis.fetch
  const visited: string[] = []
  let denyReport = false
  let malformed = false
  globalThis.fetch = async (input, init) => {
    const path = String(input)
    visited.push(path)
    if (path === '/api/v1/identity/login') return Response.json({
      personId: 'head', accessToken: 'report-token', accessExpiresAt: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh-token', refreshExpiresAt: '2099-02-01T00:00:00Z', mustChangePassword: false,
    })
    if (path === '/api/v1/admin/session') return Response.json({ code: 'FORBIDDEN', message: '无权。' }, { status: 403 })
    if (!path.startsWith('/api/v1/admin/reports/')) throw new Error('不应访问其他管理路由。')
    if (new Headers(init?.headers).get('Authorization') !== 'Bearer report-token') throw new Error('缺少人员 JWT。')
    if (denyReport) return Response.json({ code: 'FORBIDDEN', message: '只能查看直接负责部门的报表。' }, { status: 403 })
    if (path.includes('/overview')) return Response.json(malformed ? { visibleStartedCount: '1' } : {
      visibleStartedCount: 1, visibleCompletedCount: 1, visiblePendingCount: 0, visibleOverdueCount: 0,
      confidentialStartedCount: 2, confidentialCompletedCount: 1,
    })
    if (path.includes('/flows')) return Response.json({ items: [{
      flowCode: 'PUBLIC', startedCount: 1, completedCount: 1, approvedCount: 1,
      rejectedCount: 0, withdrawnCount: 0, averageDurationMinutes: 60, p90DurationMinutes: 60,
    }], confidentialStartedCount: 2, confidentialCompletedCount: 1 })
    if (path.includes('/nodes')) return Response.json({ items: [{
      flowCode: 'PUBLIC', nodeId: 'review', completedCount: 1, averageDurationMinutes: 10,
      dueCount: 1, overdueCount: 0,
    }] })
    return Response.json({ items: [{
      assigneeId: 'person-1', pendingCount: 0, completedCount: 1,
      averageDurationMinutes: 10, overdueCount: 0,
    }] })
  }
  try {
    const session = await loginAdmin('H1', 'password', 'reports')
    check('部门负责人报表会话不请求组织管理员接口，且不能进入其他管理页',
      session.scope === 'reports' && !visited.includes('/api/v1/admin/session') &&
      canReadAdminSection(session, 'reports') && !canReadAdminSection(session, 'org'))
    let blockedManagement = false
    try { await adminAccessToken() } catch { blockedManagement = true }
    const beforeOtherView = visited.length
    let rejectedOtherView = false
    try { await adminAdapter.loadView('org/people', new AbortController().signal) }
    catch { rejectedOtherView = true }
    check('报表范围令牌不会交给其他管理操作',
      blockedManagement && rejectedOtherView && visited.length === beforeOtherView)
    const result = await loadAdminReports(filter, new AbortController().signal)
    check('报表四条真实路由均携带 JWT，保密流程只保留匿名数量',
      visited.filter(path => path.startsWith('/api/v1/admin/reports/')).length === 4 &&
      visited.filter(path => path.startsWith('/api/v1/admin/reports/')).every(path => path.includes('departmentId=D1')) &&
      result.overview.confidentialStartedCount === 2 && result.flows[0]?.flowCode === 'PUBLIC' &&
      result.nodes[0]?.nodeId === 'review' && result.people[0]?.assigneeId === 'person-1')
    denyReport = true
    let forbidden = false
    try { await loadAdminReports(filter, new AbortController().signal) }
    catch (cause) { forbidden = cause instanceof ApiError && cause.status === 403 }
    check('报表接口拒绝越权时不返回部分成功指标', forbidden)
    denyReport = false
    malformed = true
    let invalid = false
    try { await loadAdminReports(filter, new AbortController().signal) }
    catch (cause) { invalid = cause instanceof Error && cause.message.includes('响应不完整') }
    check('四条接口有一条响应损坏时拒绝整组指标', invalid)
    malformed = false
    let deniedAdmin = false
    try { await loginAdmin('H1', 'password', 'admin') }
    catch (cause) { deniedAdmin = cause instanceof ApiError && cause.status === 403 }
    check('报表负责人不能使用组织管理员登录范围', deniedAdmin && !canReadAdminSection(null, 'org'))
  } finally {
    logoutAdmin()
    globalThis.fetch = originalFetch
  }
}
