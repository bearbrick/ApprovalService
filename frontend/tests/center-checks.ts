import { canSelectBatchApprove, eligibleBatchIds, filterCenterItems, visibleCenterActions } from '../src/model/center.ts'
import type { CenterFilters, CenterItem } from '../src/model/center.ts'
import { centerAdapter, CenterAdapterUnavailableError } from '../src/api/center-adapter.ts'
import { exchangeCenterEmbed, loginCenter, logoutCenter } from '../src/api/center-session.ts'
import { consumeEmbedToken, loadCenterEmbedContext, matchesEmbedTarget, postCenterChange, registeredParentOrigin } from '../src/api/center-embed.ts'
import { fetchCenterFile } from '../src/api/center-files.ts'
import { fetchCenterSignature, latestCenterSignature, uploadCenterSignature } from '../src/api/center-signatures.ts'
import { centerDetailLayout } from '../src/api/center-layout.ts'
import { loadCenterPreferences, notificationCategories, saveCenterPreference } from '../src/api/center-settings.ts'
import { createCenterOpinion, deleteCenterOpinion, loadCenterOpinions, updateCenterOpinion } from '../src/api/center-opinions.ts'
import { createCenterDelegation, delegateCenterTasks, deleteCenterDelegation, loadCenterDelegations } from '../src/api/center-delegations.ts'
import type { CenterDelegation } from '../src/api/center-delegations.ts'
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
  let requireSignature = false
  let serverUnread = true
  let failPreference = false
  const savedPreferences = new Map<string, { emailEnabled: boolean, inAppEnabled: boolean }>()
  const savedOpinions: { id: string, content: string, createdAtUtc: string }[] = []
  const savedDelegations: CenterDelegation[] = []
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
      if (path.startsWith('/api/v1/center/signatures/')) {
        if (path.endsWith('/me')) return new Response(JSON.stringify({ id: 'personal-1', sha256: 'a'.repeat(64), isPersonal: true, uploadedAt: '2026-10-25T00:00:00Z' }), { status: 200 })
        return new Response(new Uint8Array([137, 80, 78, 71]), { status: 200, headers: { 'Content-Type': 'image/png' } })
      }
      if (path.startsWith('/api/v1/center/signatures?')) {
        return new Response(JSON.stringify({ id: 'single-1', sha256: 'b'.repeat(64), isPersonal: path.includes('personal=true'), uploadedAt: '2026-10-25T00:00:00Z' }), { status: 200 })
      }
      if (path.endsWith('/files/file-visible')) {
        return new Response(new Blob(['PDF'], { type: 'application/pdf' }), { status: 200,
          headers: { 'Content-Type': 'application/pdf' } })
      }
      if (path.endsWith('/center/instances/instance-1/read')) {
        serverUnread = false
        return new Response(null, { status: 204 })
      }
      if (path.startsWith('/api/v1/notifications/preferences')) {
        if (path !== '/api/v1/notifications/preferences') {
          if (failPreference) return new Response(JSON.stringify({ code: 'CONFLICT', message: '保存失败。' }), { status: 409 })
          const category = decodeURIComponent(path.split('/').at(-1) ?? '')
          const value = JSON.parse(typeof init?.body === 'string' ? init.body : '{}') as { emailEnabled: boolean, inAppEnabled: boolean }
          savedPreferences.set(category, value)
          return new Response(JSON.stringify({ category, ...value }), { status: 200 })
        }
        return new Response(JSON.stringify(notificationCategories.map(item => ({ category: item.code,
          emailEnabled: savedPreferences.get(item.code)?.emailEnabled ?? true,
          inAppEnabled: item.code === 'dailyDigest' ? false : savedPreferences.get(item.code)?.inAppEnabled ?? true }))), { status: 200 })
      }
      if (path.startsWith('/api/v1/center/opinions')) {
        const method = init?.method ?? 'GET'
        if (method === 'GET') return new Response(JSON.stringify(savedOpinions), { status: 200 })
        if (new Headers(init?.headers).get('Authorization') === 'Bearer embed-token') {
          return new Response(JSON.stringify({ code: 'FORBIDDEN', message: '嵌入会话只能读取。' }), { status: 403 })
        }
        const content = (JSON.parse(typeof init?.body === 'string' ? init.body : '{}') as { content?: string }).content ?? ''
        const id = path.split('/').at(-1) ?? ''
        if (method === 'DELETE') {
          const index = savedOpinions.findIndex(item => item.id === id)
          if (index >= 0) savedOpinions.splice(index, 1)
          return new Response(null, { status: 204 })
        }
        if (method === 'PUT') {
          const item = savedOpinions.find(entry => entry.id === id)
          if (item) item.content = content
          return new Response(JSON.stringify(item), { status: 200 })
        }
        const item = { id: `opinion-${savedOpinions.length + 1}`, content, createdAtUtc: '2026-10-25T00:00:00Z' }
        savedOpinions.push(item)
        return new Response(JSON.stringify(item), { status: 200 })
      }
      if (path.startsWith('/api/v1/center/delegations')) {
        const method = init?.method ?? 'GET'
        if (method === 'GET') return new Response(JSON.stringify(savedDelegations), { status: 200 })
        if (new Headers(init?.headers).get('Authorization') === 'Bearer embed-token') {
          return new Response(JSON.stringify({ code: 'FORBIDDEN', message: '嵌入会话只能读取。' }), { status: 403 })
        }
        if (path.endsWith('/tasks')) {
          const payload = JSON.parse(typeof init?.body === 'string' ? init.body : '{}') as { taskIds: string[] }
          return new Response(JSON.stringify(payload.taskIds.map(taskId => ({ taskId,
            succeeded: taskId === 'task-1', code: taskId === 'task-1' ? null : 'NOT_FOUND',
            message: taskId === 'task-1' ? null : '任务不存在。' }))), { status: 200 })
        }
        if (method === 'DELETE') {
          savedDelegations.splice(savedDelegations.findIndex(item => item.id === path.split('/').at(-1)), 1)
          return new Response(null, { status: 204 })
        }
        const payload = JSON.parse(typeof init?.body === 'string' ? init.body : '{}') as CenterDelegation
        const saved = { ...payload, id: 'delegation-1', principalPersonId: 'person-1' }
        savedDelegations.push(saved)
        return new Response(JSON.stringify(saved), { status: 200 })
      }
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
            layout: { mode: 'custom', groups: [{ title: '采购信息', columns: 2, items: [
              { fieldKey: 'amount', columnSpan: 1, detailColumnWidths: {}, moneyUppercase: '贰佰元整' },
              { fieldKey: 'lines', columnSpan: 2, detailColumnWidths: { qty: 100, cost: 20 } },
              { fieldKey: 'secret', columnSpan: 1, detailColumnWidths: {} },
            ] }] },
            attachments: [{ id: 'file-visible', fileName: '票据.pdf', contentType: 'application/pdf', size: 42 }],
            tasks: [{ id: 'task-1', nodeId: 'approval', nodeName: '经理审批', assigneeId: 'person-1', status: 'pending', completedAt: null, opinion: null, signatureRequired: requireSignature, allowedActions: allowActions ? ['approve', 'bulkApprove', 'requestSupplement'] : [] },
              { id: 'task-queued', nodeId: 'approval', nodeName: '后续审批', assigneeId: 'person-1', status: 'queued', completedAt: null, opinion: null, allowedActions: [] }],
            events: [{ eventId: 'event-2', sequence: 2, kind: 'commentAdded', nodeId: 'approval', actorId: 'other', occurredAt: '2026-10-19T11:00:00Z', opinion: null }], activeNodeIds: ['approval'] }
          : { total: 1, skip: 0, take: 100, items: [{ ...summary, isUnread: serverUnread,
            canBulkApprove: allowActions, bulkApprovableTaskIds: allowActions ? ['task-1'] : [] }] }
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
    check('详情只按已授权字段绘制自定义分组、列宽和金额大写',
      detail.layout.mode === 'custom' && detail.layout.groups[0]?.title === '采购信息'
      && detail.layout.groups[0]?.items.map(entry => entry.field.key).join(',') === 'amount,lines'
      && detail.layout.groups[0]?.items[0]?.moneyUppercase === '贰佰元整'
      && JSON.stringify(detail.layout).includes('"qty":100')
      && !JSON.stringify(detail.layout).includes('secret') && !JSON.stringify(detail.layout).includes('cost'))
    check('旧服务缺少详情排版时安全回退自动排版', centerDetailLayout(undefined, detail.fields).mode === 'auto')
    const fieldSearch = await centerAdapter.loadList('todo', controller.signal, '行票据')
    check('审批列表把字段内容搜索交给 JWT 服务端且保留授权结果',
      fieldSearch.length === 1 && requests.at(-1)?.path.includes('search=%E8%A1%8C%E7%A5%A8') === true &&
      requests.at(-1)?.authorization === 'Bearer test-token')
    await centerAdapter.markRead('instance-1', controller.signal, detail.readSequence)
    const markedList = await centerAdapter.loadList('todo', controller.signal)
    check('详情显式标记使用本人 JWT，服务端确认后列表显示已读',
      list[0]?.unread === true && markedList[0]?.unread === false &&
      requests.at(-2)?.path === '/api/v1/center/instances/instance-1/read' &&
      requests.at(-2)?.method === 'POST' && requests.at(-2)?.authorization === 'Bearer test-token'
      && requests.at(-2)?.body === '{"seenThroughSequence":2}')
    const downloaded = await fetchCenterFile('file-visible')
    check('详情附件使用人员 JWT 从独立验权接口下载，令牌不出现在 URL',
      detail.attachments[0]?.fileName === '票据.pdf' && detail.attachmentNames[0] === '票据.pdf'
      && downloaded.size === 3 && requests.at(-1)?.path === '/api/v1/files/file-visible'
      && requests.at(-1)?.authorization === 'Bearer test-token')
    allowActions = true
    const permittedList = await centerAdapter.loadList('todo', controller.signal)
    const permittedDetail = await centerAdapter.loadDetail('instance-1', controller.signal)
    check('中心只映射服务端许可，并保留可批量同意的实际任务 ID',
      permittedList[0]?.allowsBatchApprove === true && permittedList[0]?.bulkTaskIds?.[0] === 'task-1'
      && permittedDetail.actionTasks?.[0]?.actions.join(',') === 'approve,supplement')
    check('委托设置可列出本人待办及排队任务，转委托仍由写接口重验',
      permittedDetail.delegationTasks?.map(task => task.taskId).join(',') === 'task-1,task-queued')
    await centerAdapter.executeAction('task-1', { action: 'approve', opinion: '同意' })
    const batch = await centerAdapter.batchApprove(['task-1'], '同意')
    check('中心写请求携带当前人员 JWT 和任务 ID，批量返回逐项结果',
      requests.slice(-2).every(request => request.authorization === 'Bearer test-token' && request.method === 'POST')
      && requests.at(-2)?.body.includes('"action":"approve"') === true
      && requests.at(-1)?.body.includes('"taskIds":["task-1"]') === true
      && batch[0]?.succeeded === true && batch[0]?.instanceId === 'instance-1')
    requireSignature = true
    const requiredDetail = await centerAdapter.loadDetail('instance-1', controller.signal)
    await centerAdapter.executeAction('task-1', { action: 'approve', signatureFileId: 'personal-1' })
    check('节点必签规则映射到操作表单且明确选用 ID 进入本次请求',
      requiredDetail.actionTasks?.[0]?.signatureRequired === true
      && requests.at(-1)?.body.includes('"signatureFileId":"personal-1"') === true)
    const preferences = await loadCenterPreferences()
    const savedPreference = await saveCenterPreference({ ...preferences[0]!, emailEnabled: false })
    const persisted = await loadCenterPreferences()
    failPreference = true
    let saveRejected = false
    try { await saveCenterPreference({ ...persisted[0]!, emailEnabled: true }) } catch { saveRejected = true }
    check('个人提醒偏好只用人员 JWT 持久读取和保存，失败不能假称成功',
      preferences.length === 8 && preferences[7]?.inAppEnabled === false
      && savedPreference.emailEnabled === false && persisted[0]?.emailEnabled === false && saveRejected
      && requests.slice(-4).every(request => request.authorization === 'Bearer test-token')
      && requests.at(-3)?.method === 'PUT' && requests.at(-3)?.body === '{"emailEnabled":false,"inAppEnabled":true}'
      && requests.at(-2)?.method === 'GET')
    const personalSignature = await latestCenterSignature()
    const singleSignature = await uploadCenterSignature(new Blob([new Uint8Array([137, 80, 78, 71])], { type: 'image/png' }), false)
    const image = await fetchCenterSignature(singleSignature.id, 'instance-1')
    check('手写签名只用人员 JWT 上传并按审批单验权取图，令牌不进 URL',
      personalSignature?.id === 'personal-1' && singleSignature.id === 'single-1' && image.type === 'image/png'
      && requests.slice(-3).every(request => request.authorization === 'Bearer test-token')
      && requests.at(-2)?.method === 'POST' && requests.at(-2)?.path.endsWith('personal=false') === true
      && requests.at(-1)?.path === '/api/v1/center/signatures/single-1?instanceId=instance-1')
    let oversizedRejected = false
    try { await uploadCenterSignature(new Blob([new Uint8Array(2 * 1024 * 1024 + 1)], { type: 'image/png' }), false) } catch { oversizedRejected = true }
    check('浏览器在发请求前拒绝超过 2 MiB 的签名 PNG', oversizedRejected &&
      requests.at(-1)?.path === '/api/v1/center/signatures/single-1?instanceId=instance-1')
    const createdOpinion = await createCenterOpinion('已核对')
    const opinions = await loadCenterOpinions()
    const updatedOpinion = await updateCenterOpinion(createdOpinion.id, '请修改')
    await deleteCenterOpinion(createdOpinion.id)
    const afterOpinionDelete = await loadCenterOpinions()
    check('常用意见由当前人员 JWT 持久管理，选择内容不直接提交任务',
      opinions.length === 1 && opinions[0]?.content === '已核对' && updatedOpinion.content === '请修改'
      && afterOpinionDelete.length === 0 && requests.slice(-5).every(request => request.authorization === 'Bearer test-token')
      && requests.slice(-5).map(request => request.method).join(',') === 'POST,GET,PUT,DELETE,GET')
    const createdDelegation = await createCenterDelegation({ delegatePersonId: 'person-2',
      startsAtUtc: '2026-10-26T00:00:00Z', endsAtUtc: '2026-10-27T00:00:00Z', flowCodes: ['PURCHASE'] })
    const delegations = await loadCenterDelegations()
    const transfers = await delegateCenterTasks(['task-1', 'missing-task'])
    await deleteCenterDelegation(createdDelegation.id)
    const afterDelegationDelete = await loadCenterDelegations()
    check('委托设置使用本人 JWT 持久读写，已有任务逐项返回且删除不伪造保存',
      delegations.length === 1 && delegations[0]?.flowCodes[0] === 'PURCHASE' &&
      transfers.length === 2 && transfers[0]?.succeeded === true &&
      transfers[1]?.succeeded === false && afterDelegationDelete.length === 0 &&
      requests.slice(-5).every(request => request.authorization === 'Bearer test-token') &&
      requests.slice(-5).map(request => request.method).join(',') === 'POST,GET,POST,DELETE,GET')
    logoutCenter()
    const embedded = await exchangeCenterEmbed('one-time')
    const context = await loadCenterEmbedContext()
    check('嵌入会话从一次性令牌交换并用嵌入 JWT 读取登记来源',
      embedded.targetPath === '#/center/approvals/instance-1'
      && requests.at(-2)?.body === '{"token":"one-time"}'
      && requests.at(-1)?.authorization === 'Bearer embed-token'
      && context.clientAppCode === 'ERP')
    const embeddedOpinions = await loadCenterOpinions()
    let embeddedWriteRejected = false
    try { await createCenterOpinion('越权') } catch { embeddedWriteRejected = true }
    check('嵌入会话可读取常用意见但不能管理', embeddedOpinions.length === 0 && embeddedWriteRejected
      && requests.slice(-2).every(request => request.authorization === 'Bearer embed-token'))
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
