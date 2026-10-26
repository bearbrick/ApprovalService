import type { InterventionCommand, InterventionRecord } from '../api/admin-intervention.ts'

/**
 * 核对当前真实选项后构建干预命令。
 * @param record 已授权读取的审批单。
 * @param action 管理操作。
 * @param taskId 所选任务。
 * @param targetPersonId 接手人员内部 ID。
 * @param targetNodeId 目标人工节点。
 * @param reason 管理员填写的原因。
 * @returns 可提交的命令；不合法时抛出中文说明。
 */
export function buildInterventionCommand(record: InterventionRecord, action: InterventionCommand['action'],
  taskId: string, targetPersonId: string, targetNodeId: string, reason: string): InterventionCommand {
  if (!['running', 'awaitingInitiator'].includes(record.status)) throw new Error('该审批单已不在途，请刷新列表。')
  const normalized = reason.trim()
  if (normalized.length < 2 || normalized.length > 200) throw new Error('请填写 2–200 字的干预原因。')
  const command: InterventionCommand = { action, expectedVersion: record.version, reason: normalized }
  if (action === 'terminate') return command
  if (record.status !== 'running') throw new Error('等待发起人处理时仅可终止审批。')
  if (action === 'reassign') {
    const task = record.tasks.find(item => item.id === taskId)
    if (!task) throw new Error('请选择当前待办或排队任务。')
    if (!targetPersonId.trim() || targetPersonId.trim().length > 100) throw new Error('请填写接手人的内部人员 ID。')
    if (targetPersonId.trim() === task.assigneeId) throw new Error('接手人员与当前审批人相同。')
    return { ...command, taskId, targetPersonId: targetPersonId.trim() }
  }
  if (!record.nodes.some(node => node.id === targetNodeId)) throw new Error('请选择锁定版本中的目标人工节点。')
  return { ...command, targetNodeId }
}
