import { canSelectBatchApprove, eligibleBatchIds, filterCenterItems, visibleCenterActions } from '../src/model/center.ts'
import type { CenterFilters, CenterItem } from '../src/model/center.ts'
import { centerAdapter, CenterAdapterUnavailableError } from '../src/api/center-adapter.ts'
import { loginCenter, logoutCenter } from '../src/api/center-session.ts'

/**
 * 供场景检查使用的完整摘要，绝不会进入产品运行时。
 * @param overrides 本场景改变的属性。
 * @returns 一条待办摘要。
 */
function item(overrides: Partial<CenterItem> = {}): CenterItem {
  return {
    id: 'a', section: 'todo', title: '采购申请', serialNo: 'SP-1', businessKey: 'PO-1', flowCode: 'PURCHASE', flowName: '采购审批',
    sourceName: '示例系统', initiatorName: '张三', currentStep: '部门审批', status: '进行中',
    createdAt: '2026-10-19T10:00:00+08:00', updatedAt: '2026-10-19T12:00:00+08:00',
    urgent: false, unread: false, allowsBatchApprove: true, allowedActions: ['approve'],
    summary: [{ key: 'supplier', label: '供应商', value: '华东材料' }], ...overrides,
  }
}

/**
 * 检查审批中心不依赖 DOM 的列表和权限展示规则。
 * @param check 统一的场景断言入口。
 * @returns 断言完成。
 */
export async function runCenterChecks(check: (name: string, value: boolean) => void): Promise<void> {
  const filters: CenterFilters = { search: '', flowCode: '', status: '', dateFrom: '', dateTo: '' }
  const normal = item()
  const urgent = item({ id: 'b', urgent: true, updatedAt: '2026-10-18T09:00:00+08:00', unread: true })
  const done = item({ id: 'c', section: 'done', allowedActions: ['undoApproval'], allowsBatchApprove: false })
  const rows = [normal, urgent, done]
  check('审批中心只筛当前分组，待办加急优先且不改变原列表',
    filterCenterItems(rows, 'todo', filters).map(row => row.id).join(',') === 'b,a' && rows[0] === normal)
  check('标题、编号、业务单号和摘要内容可搜索',
    ['采购申请', 'SP-1', 'PO-1', '华东材料'].every(search => filterCenterItems(rows, 'todo', { ...filters, search }).length === 2))
  check('流程、状态和日期筛选并保留当天记录',
    filterCenterItems(rows, 'todo', { ...filters, flowCode: 'PURCHASE', status: '进行中', dateFrom: '2026-10-19', dateTo: '2026-10-19' }).length === 2
    && filterCenterItems(rows, 'todo', { ...filters, dateTo: '2026-10-18' }).length === 0)
  check('批量选择仅包含服务端准许且仍可见的待办',
    canSelectBatchApprove(normal) && !canSelectBatchApprove(done)
    && eligibleBatchIds([normal, urgent, done], ['a', 'c', 'hidden']).join(',') === 'a'
    && eligibleBatchIds([item({ allowedActions: [], id: 'd' })], ['d']).length === 0)
  check('我发起的仅可展示服务端准许的催办与评论',
    visibleCenterActions(item({ section: 'started', allowedActions: ['approve', 'remind', 'comment'] })).join(',') === 'remind,comment'
    && visibleCenterActions(item({ section: 'cc', allowedActions: ['approve', 'comment'] })).join(',') === 'comment')
  const originalFetch = globalThis.fetch
  const requests: { path: string, authorization: string }[] = []
  const summary = {
    id: 'instance-1', serialNo: 'SP-1', flowCode: 'PURCHASE', flowName: '采购审批', businessKey: 'PO-1',
    title: '采购申请', status: 'running', clientAppCode: 'ERP', sourceName: '采购系统', initiatorId: 'person-1', initiatorName: '张三', currentStep: '经理审批', isUrgent: true,
    startedAt: '2026-10-19T10:00:00+08:00', summary: { amount: 200 }, pendingTaskIds: ['task-1'],
  }
  try {
    globalThis.fetch = async (input, init) => {
      const path = String(input)
      requests.push({ path, authorization: new Headers(init?.headers).get('Authorization') ?? '' })
      const body = path.endsWith('/identity/login')
        ? { personId: 'person-1', accessToken: 'test-token', accessExpiresAt: '2099-01-01T00:00:00Z', refreshToken: 'refresh', refreshExpiresAt: '2099-01-02T00:00:00Z', mustChangePassword: false }
        : path.includes('instances/instance-1')
          ? { instance: summary, data: { amount: 200 }, tasks: [{ id: 'task-1', nodeId: 'approval', nodeName: '经理审批', assigneeId: 'person-1', status: 'pending', completedAt: null, opinion: null }], events: [], activeNodeIds: ['approval'] }
          : { total: 1, skip: 0, take: 100, items: [summary] }
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }
    const controller = new AbortController()
    let denied = false
    try { await centerAdapter.loadList('todo', controller.signal) } catch (cause) { denied = cause instanceof CenterAdapterUnavailableError }
    check('未登录时不会向中心接口发送匿名查询', denied && requests.length === 0)
    await loginCenter('employee', 'password')
    const list = await centerAdapter.loadList('todo', controller.signal)
    const detail = await centerAdapter.loadDetail('instance-1', controller.signal)
    check('审批中心用人员令牌读取真实列表和详情，不向匿名端点取伪数据',
      requests.length === 3 && requests.slice(1).every(request => request.authorization === 'Bearer test-token')
      && list.length === 1 && list[0]?.title === '采购申请' && list[0]?.initiatorName === '张三'
      && list[0]?.sourceName === '采购系统' && detail.item.currentStep === '经理审批')
    check('中心适配仅使用服务端裁剪字段，缺少许可协议时不会凭待办推断可操作',
      list[0]?.summary[0]?.value === '200' && detail.fields[0]?.value === '200'
      && detail.item.allowedActions.length === 0 && !detail.item.allowsBatchApprove)
  } finally {
    globalThis.fetch = originalFetch
    logoutCenter()
  }
}
