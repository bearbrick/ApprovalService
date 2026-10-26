import { ADMIN_VIEWS, adminCellText, filterAdminRows, resolveAdminView } from '../src/model/admin.ts'
import type { AdminRow } from '../src/model/admin.ts'
import { ADMIN_TABS } from '../src/model/navigation.ts'
import { adminAdapter, AdminAdapterUnavailableError } from '../src/api/admin-adapter.ts'
import { adminTodayQuery, auditViewData, bindingsViewData, clientAppsViewData, directoryViewData, outboxViewData, overviewViewData } from '../src/model/admin-data.ts'

/**
 * 检查管理员页面的路径覆盖、安全列与未接入边界。
 * @param check 统一的场景断言入口。
 * @returns 场景执行完成。
 */
export async function runAdminChecks(check: (name: string, value: boolean) => void): Promise<void> {
  check('管理后台所有既有一级入口都有正式页面定义',
    ['overview', 'flows', 'runtime'].every(section => !!resolveAdminView(section, '')))
  check('组织、应用、出站、统计和系统管理的全部页签都有页面定义',
    Object.entries(ADMIN_TABS).every(([section, tabs]) => tabs.every(tab => !!resolveAdminView(section, tab))))
  check('管理页面编码唯一且列不登记密码、密钥或连接串',
    new Set(ADMIN_VIEWS.map(view => view.key)).size === ADMIN_VIEWS.length
    && ADMIN_VIEWS.every(view => view.columns.every(column => !/(password|secret|apiKey|connectionString|privateKey)/i.test(column.key))))
  const rows: AdminRow[] = [
    { id: 'one', cells: { name: '采购流程', code: 'PURCHASE', hiddenSecret: 'private-token' } },
    { id: 'two', cells: { name: '费用流程', code: 'EXPENSE', hiddenSecret: 'other-token' } },
  ]
  const columns = [{ key: 'name', title: '名称' }, { key: 'code', title: '编码' }]
  check('管理搜索只匹配可见且已授权的列，不能通过隐藏字段命中',
    filterAdminRows(rows, columns, '采购').map(row => row.id).join(',') === 'one'
    && filterAdminRows(rows, columns, 'private-token').length === 0
    && rows.length === 2)
  check('管理表格未定义的列用占位符，不回退到原始行对象', adminCellText(rows[0]!, { key: 'status', title: '状态' }) === '—')
  const directory = {
    departments: [{ id: 'd1', code: 'D1', name: '业务部', parentName: null, managerName: '甲', source: 'approvalService' }],
    people: [{ id: 'p1', employeeNumber: 'E1', name: '乙', departments: ['业务部'], position: 'clerk', isActive: true }],
  }
  check('管理组织目录将真实部门和人员映射到安全列',
    directoryViewData('org/departments', directory).rows[0]?.cells.manager === '甲'
    && directoryViewData('org/people', directory).rows[0]?.cells.departments === '业务部')
  const appRows = clientAppsViewData([{ code: 'demo', name: '示例系统', allowedFlowCodes: ['EXPENSE'], isEnabled: true }]).rows
  check('应用列表映射只保留安全字段，不制造密钥或更新时间',
    appRows[0]?.cells.flows === 'EXPENSE' && appRows[0]?.cells.updatedAt === '—'
    && !Object.keys(appRows[0]?.cells ?? {}).some(key => /secret|apiKey|password/i.test(key)))
  check('绑定列表仅展示应用、外部 ID 和内部目标名称',
    bindingsViewData([{ clientAppCode: 'demo', kind: 'person', externalId: 'u1', internalId: 'p1',
      targetName: '乙', method: 'manual' }]).rows[0]?.cells.target === '乙（p1）')
  check('操作日志列表不展示原始变更快照',
    auditViewData([{ occurredAt: '2026-10-23T00:00:00Z', actorId: 'p1', action: 'updated',
      target: 'clientApp:demo', reason: '试点接入' }]).rows[0]?.cells.reason === '试点接入'
    && auditViewData([{ occurredAt: '2026-10-23T00:00:00Z', actorId: 'p1', action: 'updated',
      target: 'clientApp:demo', reason: '试点接入' }]).rows[0]?.cells.target === '接入应用：demo')
  check('待发列表仅映射安全元数据并保留回调事件 ID',
    outboxViewData([{ eventId: 'e1', clientAppCode: 'demo', kind: 'callback', status: 'pending',
      attempts: 2, availableAt: '2026-10-23T00:00:00Z', errorCode: 'TimeoutException' }]).rows[0]?.cells.kind === '回调'
    && outboxViewData([{ eventId: 'e1', clientAppCode: 'demo', kind: 'callback', status: 'pending',
      attempts: 2, availableAt: '2026-10-23T00:00:00Z', errorCode: 'TimeoutException' }]).rows[0]?.cells.eventId === 'e1')
  const today = new URLSearchParams(adminTodayQuery(new Date(2026, 9, 26, 18, 30)).slice(1))
  check('工作台今日使用本地零点到次日零点的 UTC 半开区间',
    today.get('fromUtc') === new Date(2026, 9, 26).toISOString()
    && today.get('untilUtc') === new Date(2026, 9, 27).toISOString())
  const overview = overviewViewData({ visibleStartedCount: 3, confidentialStartedCount: 2,
    visibleCompletedCount: 1, confidentialCompletedCount: 1, visiblePendingCount: 4, visibleOverdueCount: 2 }, [
    { eventId: 'action', clientAppCode: 'erp', kind: 'engineEvent', status: 'needsAttention',
      attempts: 10, availableAt: '2026-10-26T01:00:00Z', errorCode: 'BUSINESS_HOOK_FAILED' },
    { eventId: 'callback', clientAppCode: 'erp', kind: 'callback', status: 'pending',
      attempts: 2, availableAt: '2026-10-26T02:00:00Z', errorCode: 'TIMEOUT' },
    { eventId: 'done', clientAppCode: 'erp', kind: 'callback', status: 'completed',
      attempts: 1, availableAt: '2026-10-26T00:00:00Z', errorCode: 'OLD_ERROR' },
  ])
  check('工作台合并保密匿名数量，仅显示需关注出站项且不泄露错误正文',
    overview.metrics.find(metric => metric.key === 'startedToday')?.value === '5'
    && overview.metrics.find(metric => metric.key === 'completedToday')?.value === '2'
    && overview.metrics.find(metric => metric.key === 'pendingVisible')?.value === '4'
    && overview.rows.map(row => row.id).join(',') === 'action,callback'
    && overview.rows.every(row => !Object.keys(row.cells).some(key => /error|payload|secret/i.test(key))))
  let unavailable = false
  try { await adminAdapter.loadView('system/storage', new AbortController().signal) } catch (error) { unavailable = error instanceof AdminAdapterUnavailableError }
  check('未接入的管理页面明确拒绝，不返回虚构统计或密钥', unavailable)
}
