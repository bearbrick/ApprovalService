import { buildInterventionCommand } from '../src/model/admin-intervention.ts'
import { isInterventionRecord } from '../src/api/admin-intervention.ts'
import type { InterventionRecord } from '../src/api/admin-intervention.ts'
import { canReadAdminSection } from '../src/api/admin-session.ts'
import type { AdminSession } from '../src/api/admin-session.ts'

/**
 * 检查真实运行选项、命令边界与权限入口。
 * @param check 检查记录器。
 */
export function runInterventionChecks(check: (name: string, value: boolean) => void): void {
  const record: InterventionRecord = {
    id: 'instance', title: '测试', serialNo: 'AP-1', flowCode: 'FLOW', status: 'running', version: 3,
    flowVersionId: 'published-1', tasks: [{ id: 'task/a', nodeId: 'node', name: '审批', assigneeId: 'old', status: 'pending' }],
    nodes: [{ id: 'node', name: '审批' }],
  }
  check('干预响应要求完整版本与真实选项', isInterventionRecord(record) &&
    !isInterventionRecord({ ...record, version: 0 }) && !isInterventionRecord({ ...record, nodes: [{}] }))
  const changed = buildInterventionCommand(record, 'reassign', 'task/a', ' new ', '', ' 工作交接 ')
  check('改派仅携带当前任务和读取修订，不改变发布版本', changed.taskId === 'task/a' &&
    changed.targetPersonId === 'new' && changed.expectedVersion === 3 && changed.reason === '工作交接' &&
    changed.targetNodeId === undefined && record.flowVersionId === 'published-1')
  for (const [name, action, task, person, node, reason] of [
    ['原因必填', 'terminate', '', '', '', ' '],
    ['拒绝失效任务', 'reassign', 'missing', 'new', '', '工作交接'],
    ['拒绝相同接手人', 'reassign', 'task/a', 'old', '', '工作交接'],
    ['拒绝任意跳转目标', 'jump', '', '', 'gateway', '调整路线'],
  ] as const) {
    let rejected = false
    try { buildInterventionCommand(record, action, task, person, node, reason) }
    catch { rejected = true }
    check('运行干预' + name, rejected)
  }
  const jump = buildInterventionCommand(record, 'jump', '', '', 'node', '调整路线')
  const terminate = buildInterventionCommand({ ...record, status: 'awaitingInitiator' }, 'terminate', '', '', '', '业务取消')
  check('跳转只携带人工节点而终止不携带人员和任务', jump.targetNodeId === 'node' &&
    jump.taskId === undefined && terminate.taskId === undefined && terminate.targetPersonId === undefined)
  let waitingRejected = false
  try { buildInterventionCommand({ ...record, status: 'awaitingInitiator' }, 'jump', '', '', 'node', '调整路线') }
  catch { waitingRejected = true }
  check('等待发起人时禁止改派与跳转', waitingRejected)
  const flowSession = { tokens: { mustChangePassword: false } } as AdminSession
  check('普通登录可进入管理页面请求授权，未登录不能进入',
    canReadAdminSection(flowSession, 'runtime') && !canReadAdminSection(null, 'org'))
}
