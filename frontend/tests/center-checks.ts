import { canSelectBatchApprove, eligibleBatchIds, filterCenterItems, visibleCenterActions } from '../src/model/center.ts'
import type { CenterFilters, CenterItem } from '../src/model/center.ts'
import { centerAdapter, CenterAdapterUnavailableError } from '../src/api/center-adapter.ts'
import { exchangeCenterEmbed, loginCenter, logoutCenter } from '../src/api/center-session.ts'
import { consumeEmbedToken, loadCenterEmbedContext, matchesEmbedTarget, postCenterChange, registeredParentOrigin } from '../src/api/center-embed.ts'
import type { CenterChangeMessage } from '../src/api/center-embed.ts'

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
  let cleanedHash = ''
  const pending = consumeEmbedToken('#/center/approvals/instance-1?embedToken=one-time&view=compact', hash => { cleanedHash = hash })
  check('一次性令牌从片段和历史条目先清除，再交给交换接口',
    pending?.token === 'one-time' && pending.cleanHash === '#/center/approvals/instance-1?view=compact'
    && cleanedHash === pending.cleanHash)
  let duplicateRemoved = false
  let duplicateRejected = false
  try {
    consumeEmbedToken('#/center/approvals/instance-1?embedToken=first&embedToken=second', () => { duplicateRemoved = true })
  } catch { duplicateRejected = true }
  check('重复嵌入令牌也先从地址清除且拒绝兑换', duplicateRemoved && duplicateRejected)
  check('嵌入会话只能打开服务端绑定的片段路由',
    matchesEmbedTarget('#/center/approvals/instance-1', '#/center/approvals/instance-1')
    && !matchesEmbedTarget('#/center/approvals/other', '#/center/approvals/instance-1')
    && !matchesEmbedTarget(null, '#/center/approvals/instance-1'))
  const originalFetch = globalThis.fetch
  const requests: { path: string, authorization: string, method: string, body: string }[] = []
  let allowActions = false
  const summary = {
    id: 'instance-1', serialNo: 'SP-1', flowCode: 'PURCHASE', flowName: '采购审批', businessKey: 'PO-1',
    title: '采购申请', status: 'running', clientAppCode: 'ERP', sourceName: '采购系统', initiatorId: 'person-1', initiatorName: '张三', currentStep: '经理审批', isUrgent: true,
    startedAt: '2026-10-19T10:00:00+08:00', summary: { amount: 200, lines: [{ qty: 2 }] },
    summaryFieldDefinitions: [{ key: 'amount', name: '金额', type: 'number', children: [] },
      { key: 'lines', name: '明细', type: 'detailTable', children: [{ key: 'qty', name: '数量', type: 'number', children: [] }] }],
    pendingTaskIds: ['task-1'],
  }
  try {
    globalThis.fetch = async (input, init) => {
      const path = String(input)
      requests.push({ path, authorization: new Headers(init?.headers).get('Authorization') ?? '',
        method: init?.method ?? 'GET', body: typeof init?.body === 'string' ? init.body : '' })
      const body = path.endsWith('/identity/login')
        ? { personId: 'person-1', accessToken: 'test-token', accessExpiresAt: '2099-01-01T00:00:00Z', refreshToken: 'refresh', refreshExpiresAt: '2099-01-02T00:00:00Z', mustChangePassword: false }
        : path.endsWith('/embed-tokens/exchange')
          ? { personId: 'person-1', accessToken: 'embed-token', accessExpiresAt: '2099-01-01T00:00:00Z', refreshToken: 'embed-refresh', refreshExpiresAt: '2099-01-02T00:00:00Z', mustChangePassword: false, targetPath: '#/center/approvals/instance-1' }
        : path.endsWith('/center/embed-context')
          ? { clientAppCode: 'ERP', allowedOrigins: ['https://erp.example.com'] }
        : path.endsWith('/tasks/batch-approve')
          ? { items: [{ taskId: 'task-1', succeeded: true, instanceId: 'instance-1' }] }
          : path.endsWith('/tasks/task-1/actions')
            ? { taskId: 'task-1', instanceId: 'instance-1' }
        : path.includes('instances/instance-1')
          ? { instance: { ...summary, canBulkApprove: allowActions, bulkApprovableTaskIds: allowActions ? ['task-1'] : [] },
            data: { amount: 200, lines: [{ qty: 2 }] }, fieldDefinitions: summary.summaryFieldDefinitions,
            tasks: [{ id: 'task-1', nodeId: 'approval', nodeName: '经理审批', assigneeId: 'person-1', status: 'pending', completedAt: null, opinion: null, allowedActions: allowActions ? ['approve', 'bulkApprove', 'requestSupplement'] : [] }], events: [], activeNodeIds: ['approval'] }
          : { total: 1, skip: 0, take: 100, items: [{ ...summary, canBulkApprove: allowActions, bulkApprovableTaskIds: allowActions ? ['task-1'] : [] }] }
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
    check('中心用锁定版本的名称展示摘要与结构化明细，不把明细渲染为原始 JSON',
      list[0]?.summary[0]?.label === '金额' && list[0]?.summary[1]?.label === '明细'
      && list[0]?.summary[1]?.value === '1 条明细' && detail.fields[1]?.columns?.[0]?.label === '数量'
      && detail.fields[1]?.rows?.[0]?.[0] === '2')
    allowActions = true
    const permittedList = await centerAdapter.loadList('todo', controller.signal)
    const permittedDetail = await centerAdapter.loadDetail('instance-1', controller.signal)
    check('中心只映射服务端许可，并保留可批量同意的实际任务 ID',
      permittedList[0]?.allowsBatchApprove === true && permittedList[0]?.bulkTaskIds?.[0] === 'task-1'
      && permittedDetail.actionTasks?.[0]?.actions.join(',') === 'approve,supplement')
    await centerAdapter.executeAction('task-1', { action: 'approve', opinion: '同意' })
    const batch = await centerAdapter.batchApprove(['task-1'], '同意')
    check('中心写请求携带当前人员 JWT 和任务 ID，批量返回逐项结果',
      requests.slice(-2).every(request => request.authorization === 'Bearer test-token' && request.method === 'POST')
      && requests.at(-2)?.body.includes('"action":"approve"') === true
      && requests.at(-1)?.body.includes('"taskIds":["task-1"]') === true
      && batch[0]?.succeeded === true && batch[0]?.instanceId === 'instance-1')
    logoutCenter()
    const embedded = await exchangeCenterEmbed('one-time')
    const context = await loadCenterEmbedContext()
    check('嵌入会话从一次性令牌交换并用嵌入 JWT 读取登记来源',
      embedded.targetPath === '#/center/approvals/instance-1'
      && requests.at(-2)?.body === '{"token":"one-time"}'
      && requests.at(-1)?.authorization === 'Bearer embed-token'
      && context.clientAppCode === 'ERP')
    const sent: { message: CenterChangeMessage, targetOrigin: string }[] = []
    const sink = { postMessage(message: CenterChangeMessage, targetOrigin: string): void { sent.push({ message, targetOrigin }) } }
    const permitted = postCenterChange(context, 'https://erp.example.com/orders/1', sink, 'instance-1', 'task-1')
    const deniedMessage = postCenterChange(context, 'https://evil.example.com/', sink, 'instance-1', 'task-1')
    check('完成消息只发精确登记来源且不携带令牌或业务数据',
      permitted && !deniedMessage && sent.length === 1 && sent[0]?.targetOrigin === 'https://erp.example.com'
      && JSON.stringify(sent[0]?.message) === '{"instanceId":"instance-1","taskId":"task-1","status":"changed"}'
      && registeredParentOrigin(context, 'https://erp.example.com.evil.test/') === null)
  } finally {
    globalThis.fetch = originalFetch
    logoutCenter()
  }
}
