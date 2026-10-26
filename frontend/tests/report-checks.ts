import { ApiError } from '../src/api/http.ts'
import { adminAccessToken, verifyAdminSession, adminSession, canReadAdminSection, loginAdmin, logoutAdmin } from '../src/api/admin-session.ts'
import { downloadAdminReport, loadAdminReports, reportQuery } from '../src/api/admin-reports.ts'
import type { ReportFilter } from '../src/api/admin-reports.ts'
import { adminAdapter } from '../src/api/admin-adapter.ts'

/**
 * 在无浏览器条件下验证报表筛选、JWT 范围、四条 JSON 响应及 Excel 下载。
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
  let exportMode: 'ok' | 'unauthorized' | 'network' | 'wrong-type' | 'broken' = 'ok'
  globalThis.fetch = async (input, init) => {
    const path = String(input)
    visited.push(path)
    if (path === '/api/v1/identity/login') return Response.json({
      personId: 'head', accessToken: 'report-token', accessExpiresAt: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh-token', refreshExpiresAt: '2099-02-01T00:00:00Z', mustChangePassword: false,
    })
    if (path === '/api/v1/admin/session') return Response.json({ code: 'FORBIDDEN', message: '无权。' }, { status: 403 })
    if (!path.startsWith('/api/v1/admin/reports/')) return Response.json({ code: 'FORBIDDEN', message: '无组织管理权限。' }, { status: 403 })
    if (new Headers(init?.headers).get('Authorization') !== 'Bearer report-token') throw new Error('缺少人员 JWT。')
    if (denyReport) return Response.json({ code: 'FORBIDDEN', message: '只能查看直接负责部门的报表。' }, { status: 403 })
    if (path.includes('/export')) {
      if (exportMode === 'unauthorized') return Response.json({ code: 'UNAUTHORIZED', message: '请先登录。' }, { status: 401 })
      if (exportMode === 'network') throw new Error('连接失败')
      if (exportMode === 'wrong-type') return new Response('PK\u0003\u0004', { headers: { 'Content-Type': 'text/plain' } })
      return new Response(exportMode === 'broken' ? '损坏内容' : 'PK\u0003\u0004XLSX', {
        headers: { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
      })
    }
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
    check('报表入口普通登录不预先要求组织管理员资格', session.tokens.personId === 'head'
      && !visited.includes('/api/v1/admin/session') && canReadAdminSection(session, 'reports'))
    let rejectedOtherView = false
    try { await adminAdapter.loadView('org/people', new AbortController().signal) }
    catch (cause) { rejectedOtherView = cause instanceof ApiError && cause.status === 403 }
    check('报表人员访问组织接口由服务端拒绝且保留登录', rejectedOtherView
      && adminSession()?.tokens.personId === 'head' && await adminAccessToken('reports') === 'report-token')
    const result = await loadAdminReports(filter, new AbortController().signal)
    check('报表四条真实路由均携带 JWT，保密流程只保留匿名数量',
      visited.filter(path => path.startsWith('/api/v1/admin/reports/')).length === 4 &&
      visited.filter(path => path.startsWith('/api/v1/admin/reports/')).every(path => path.includes('departmentId=D1')) &&
      result.overview.confidentialStartedCount === 2 && result.flows[0]?.flowCode === 'PUBLIC' &&
      result.nodes[0]?.nodeId === 'review' && result.people[0]?.assigneeId === 'person-1')
    const workbook = await downloadAdminReport(filter, new AbortController().signal)
    const exportPath = visited.at(-1) ?? ''
    check('Excel 导出复用当前报表 JWT、UTC 时间与全部维度筛选',
      exportPath.startsWith('/api/v1/admin/reports/export?') && exportPath.includes('departmentId=D1') &&
      exportPath.includes('flowCode=PUBLIC') && exportPath.includes('clientAppCode=ERP') &&
      exportPath.includes('fromUtc=') && exportPath.includes('untilUtc=') &&
      workbook.fileName === 'approval-report.xlsx' && workbook.blob.size === 8)
    denyReport = true
    let forbidden = false
    try { await loadAdminReports(filter, new AbortController().signal) }
    catch (cause) { forbidden = cause instanceof ApiError && cause.status === 403 }
    check('报表接口拒绝越权时不返回部分成功指标', forbidden)
    let exportForbidden = false
    try { await downloadAdminReport(filter, new AbortController().signal) }
    catch (cause) { exportForbidden = cause instanceof ApiError && cause.status === 403 && cause.message.includes('直接负责部门') }
    check('Excel 导出被服务端拒绝时保留中文权限原因且不返回文件', exportForbidden)
    denyReport = false
    exportMode = 'unauthorized'
    let exportUnauthorized = false
    try { await downloadAdminReport(filter, new AbortController().signal) }
    catch (cause) { exportUnauthorized = cause instanceof ApiError && cause.status === 401 }
    exportMode = 'network'
    let exportNetwork = false
    try { await downloadAdminReport(filter, new AbortController().signal) }
    catch (cause) { exportNetwork = cause instanceof ApiError && cause.code === 'NETWORK_ERROR' }
    check('Excel 下载区分登录失效与网络失败', exportUnauthorized && exportNetwork)
    exportMode = 'wrong-type'
    let wrongType = false
    try { await downloadAdminReport(filter, new AbortController().signal) }
    catch (cause) { wrongType = cause instanceof ApiError && cause.code === 'INVALID_RESPONSE' }
    exportMode = 'broken'
    let broken = false
    try { await downloadAdminReport(filter, new AbortController().signal) }
    catch (cause) { broken = cause instanceof ApiError && cause.code === 'INVALID_RESPONSE' }
    check('Excel 下载拒绝错误媒体类型和损坏的文件头', wrongType && broken)
    exportMode = 'ok'
    const beforeInvalidExport = visited.length
    let invalidExportRange = false
    try { await downloadAdminReport({ ...filter, untilLocal: filter.fromLocal }, new AbortController().signal) }
    catch (cause) { invalidExportRange = cause instanceof Error && cause.message.includes('时间范围') }
    check('无效时间不会发出 Excel 导出请求', invalidExportRange && visited.length === beforeInvalidExport)
    const cancelled = new AbortController()
    cancelled.abort()
    const beforeCancelledExport = visited.length
    let stoppedExport = false
    try { await downloadAdminReport(filter, cancelled.signal) }
    catch (cause) { stoppedExport = cause instanceof ApiError && cause.kind === 'cancelled' }
    check('报表页面撤销后不会发出 Excel 导出请求', stoppedExport && visited.length === beforeCancelledExport)
    malformed = true
    let invalid = false
    try { await loadAdminReports(filter, new AbortController().signal) }
    catch (cause) { invalid = cause instanceof Error && cause.message.includes('响应不完整') }
    check('四条接口有一条响应损坏时拒绝整组指标', invalid)
    malformed = false
    let deniedAdmin = false
    try { await verifyAdminSession() }
    catch (cause) { deniedAdmin = cause instanceof ApiError && cause.status === 403 }
    check('报表负责人由服务端拒绝组织管理权限，未登录守卫仍有效', deniedAdmin && !canReadAdminSection(null, 'org'))
  } finally {
    logoutAdmin()
    globalThis.fetch = originalFetch
  }
}
