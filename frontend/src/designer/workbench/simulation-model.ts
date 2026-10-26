import type { FlowDefinition, FlowFieldDefinition, FlowNode } from '../../model/flow.ts'
import { sampleSubmission } from '../data/data-model.ts'

/**
 * 只在测试会话中使用的假输入。
 */
export interface SimulationInput {
  /**
   * 当前完整草稿。
   */
  definition: FlowDefinition
  /**
   * 可编辑的假业务数据对象。
   */
  data: Record<string, unknown>
  /**
   * 假发起人内部 ID。
   */
  initiatorId: string
  /**
   * 人工节点到假处理人 ID 的映射。
   */
  fakeAssignees: Record<string, string[]>
}

/**
 * 返回需要假处理人的节点，抄送也由同一模拟快照解析。
 * @param definition 当前流程草稿。
 * @returns 按画布顺序排列的人工节点。
 */
export function simulationManualNodes(definition: FlowDefinition): FlowNode[] {
  return definition.canvas.nodes.filter(node => ['approval', 'handling', 'cc'].includes(node.type))
}

/**
 * 只列出当前节点显式可编辑的顶层审批人字段。
 * @param definition 创建内存会话的草稿。
 * @param nodeId 当前人工节点 ID。
 * @returns 按审批数据定义排序的可填写字段。
 */
export function simulationEditableFields(definition: FlowDefinition, nodeId: string): FlowFieldDefinition[] {
  const node = definition.canvas.nodes.find(item => item.id === nodeId)
  const allowed = new Set(node?.fieldPermissions.filter(item => item.access === 'editable').map(item => item.fieldKey) ?? [])
  return definition.data.fields.filter(field => field.approverInput && allowed.has(field.key))
}

/**
 * 在发送模拟命令前校验假填写 JSON，避免误把业务字段送入审批人输入。
 * @param text 当前任务的假填写文本。
 * @param fields 当前节点显式可填写的字段。
 * @returns 空输入不提交字段，否则返回受限字段对象。
 */
export function parseSimulationFieldValues(text: string, fields: FlowFieldDefinition[]): Record<string, unknown> | undefined {
  if (!text.trim()) return undefined
  let parsed: unknown
  try { parsed = JSON.parse(text) } catch { throw new Error('假审批字段必须是有效的 JSON 对象。') }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('假审批字段必须是 JSON 对象。')
  }
  const allowed = new Set(fields.map(field => field.key))
  if (Object.keys(parsed).some(key => !allowed.has(key))) {
    throw new Error('假审批字段包含当前节点不可填写的字段。')
  }
  return Object.keys(parsed).length ? parsed as Record<string, unknown> : undefined
}

/**
 * 从当前字段定义准备可以修改的假数据，不生成审批人填写值或计算字段。
 * @param definition 当前流程草稿。
 * @returns 已格式化的 JSON 文本。
 */
export function simulationExampleData(definition: FlowDefinition): string {
  return JSON.stringify(sampleSubmission(definition.data), null, 2)
}

/**
 * 校验编辑器输入并建立发送给内存模拟接口的请求体。
 * @param definition 当前完整草稿。
 * @param dataText 假数据 JSON。
 * @param initiatorId 假发起人 ID。
 * @param peopleText 各人工节点的假人员输入。
 * @returns 已校验的请求体。
 */
export function buildSimulationInput(definition: FlowDefinition, dataText: string, initiatorId: string,
  peopleText: Record<string, string>): SimulationInput {
  const initiator = initiatorId.trim()
  if (!definition.code.trim() || !initiator || initiator.length > 128) {
    throw new Error('请填写流程编码和不超过 128 字的假发起人 ID。')
  }
  let data: unknown
  try { data = JSON.parse(dataText) } catch { throw new Error('假业务数据必须是有效的 JSON 对象。') }
  if (!data || typeof data !== 'object' || Array.isArray(data)) {
    throw new Error('假业务数据必须是 JSON 对象。')
  }
  const fakeAssignees: Record<string, string[]> = {}
  for (const node of simulationManualNodes(definition)) {
    const people = [...new Set((peopleText[node.id] ?? '').split(/[,，\n]/)
      .map(value => value.trim()).filter(Boolean))]
    if (people.length < 1 || people.length > 50 || people.some(value => value.length > 128)) {
      throw new Error(`请为“${node.name}”填写 1 至 50 个假处理人 ID。`)
    }
    fakeAssignees[node.id] = people
  }
  return { definition, data: data as Record<string, unknown>, initiatorId: initiator, fakeAssignees }
}
