/**
 * 与服务端 FlowCompiler 一致的发布前静态校验镜像；最终发布仍以服务端结果为准。
 */
import { flowFieldTypes, flowGatewayDirections, flowNodeTypes, flowReturnModes, flowSchemaVersion } from '../../model/flow.ts'
import type { FlowCanvas, FlowDefinition, FlowEdge, FlowFieldDefinition, FlowNode } from '../../model/flow.ts'
import { parseStaticExpression } from './expression-static.ts'
import { evaluateConstantExpression } from './expression-constant.ts'

/**
 * 可定位到节点、连线或字段的发布问题。
 */
export interface CanvasIssue {
  /**
   * 相关元素 ID；流程整体问题为 null。
   */
  elementId: string | null
  /**
   * 给设计人员看的中文问题说明。
   */
  message: string
}

/**
 * 网关节点类型集合。
 */
const gateways = new Set<string>([flowNodeTypes.ExclusiveGateway, flowNodeTypes.InclusiveGateway, flowNodeTypes.ParallelGateway])

/**
 * 所有后端已支持的字段类型。
 */
const fieldTypes = new Set<string>(Object.values(flowFieldTypes))

/**
 * 从节点沿连线查找可达节点，允许回路且不会无限循环。
 * @param start 起点 ID。
 * @param adjacency 邻接表。
 * @param excluded 不允许经过的节点。
 * @returns 可达节点 ID 集合。
 */
function traverse(start: string, adjacency: Map<string, string[]>, excluded?: string): Set<string> {
  const seen = new Set<string>()
  if (start === excluded || !adjacency.has(start)) return seen
  const pending = [start]
  seen.add(start)
  for (let cursor = 0; cursor < pending.length; cursor++) {
    for (const next of adjacency.get(pending[cursor]!) ?? []) {
      if (next !== excluded && !seen.has(next)) { seen.add(next); pending.push(next) }
    }
  }
  return seen
}

/**
 * 遍历字段定义并收集所有完整字段路径。
 * @param fields 当前层级的字段。
 * @param issues 问题集合。
 * @param prefix 父级字段路径。
 * @param paths 字段路径集合。
 */
function collectFields(fields: FlowFieldDefinition[], issues: CanvasIssue[], prefix: string, paths: Set<string>): void {
  for (const field of fields) {
    if (!field.key?.trim()) { issues.push({ elementId: prefix || null, message: '审批数据字段缺少 key。' }); continue }
    const path = prefix ? `${prefix}.${field.key}` : field.key
    if (paths.has(path)) issues.push({ elementId: path, message: `审批数据字段“${path}”重复。` })
    paths.add(path)
    if (!fieldTypes.has(field.type)) issues.push({ elementId: path, message: `审批数据字段“${path}”的类型不受支持。` })
    if (field.type !== flowFieldTypes.DetailTable && field.children?.length) issues.push({ elementId: path, message: `非明细表字段“${path}”不能包含子字段。` })
    if (field.type === flowFieldTypes.DetailTable && !field.children?.length) issues.push({ elementId: path, message: `明细表字段“${path}”至少要有一个子字段。` })
    collectFields(field.children ?? [], issues, path, paths)
  }
}

/**
 * 核对表达式内所有字段引用都已在审批数据中定义。
 * @param expression 条件或计算表达式。
 * @param elementId 连线或字段 ID。
 * @param paths 已定义字段路径。
 * @param issues 问题集合。
 */
function checkReferences(expression: string | null | undefined, elementId: string, paths: Set<string>, issues: CanvasIssue[]): void {
  if (!expression?.trim()) return
  let references: string[]
  try {
    const result = parseStaticExpression(expression)
    references = result.references
    if (references.length === 0) evaluateConstantExpression(result.tree)
  } catch (error) {
    const detail = error instanceof Error ? error.message : '语法无效。'
    issues.push({ elementId, message: `表达式“${elementId}”无效：${detail}` })
    return
  }
  for (const key of references) {
    if (!paths.has(key) && !['initiator.department', 'initiator.role', 'initiator.level'].includes(key)) {
      issues.push({ elementId, message: `表达式“${elementId}”引用了不存在的字段“${key}”。` })
    }
  }
}

/**
 * 检查发布图的节点、连线、条件与网关配对；错误可直接高亮在画布上。
 * @param definition 完整流程定义。
 * @returns 全部发布问题。
 */
export function validateCanvasDefinition(definition: FlowDefinition): CanvasIssue[] {
  const issues: CanvasIssue[] = []
  const add = (elementId: string | null, message: string): void => { issues.push({ elementId, message }) }
  if (definition.schemaVersion !== flowSchemaVersion) add(null, `流程定义格式版本不受支持：${definition.schemaVersion}。`)
  if (!definition.code?.trim()) add(null, '流程编码不能为空。')
  if (!definition.name?.trim()) add(null, '流程名称不能为空。')
  if (definition.withdrawBeforeFirstDecision !== undefined && typeof definition.withdrawBeforeFirstDecision !== 'boolean') add(null, '发起人撤回限制必须为布尔值。')
  if (definition.returnMode !== undefined && definition.returnMode !== flowReturnModes.Restart && definition.returnMode !== flowReturnModes.ReturnNode) add(null, '退回后重新提交方式只能是 restart 或 returnNode。')
  const canvas: FlowCanvas | undefined = definition.canvas
  if (!canvas) { add(null, '流程缺少自由画布。'); return issues }
  const nodes = canvas.nodes ?? []
  const edges = canvas.edges ?? []
  const nodeMap = new Map<string, FlowNode>()
  const edgeIds = new Set<string>()
  const outgoing = new Map<string, string[]>()
  const incoming = new Map<string, string[]>()
  for (const node of nodes) {
    if (!node.id?.trim()) add(null, '节点缺少 ID。')
    else if (nodeMap.has(node.id)) add(node.id, `节点 ID“${node.id}”重复。`)
    else { nodeMap.set(node.id, node); outgoing.set(node.id, []); incoming.set(node.id, []) }
    if (![...Object.values(flowNodeTypes)].includes(node.type)) add(node.id, `节点“${node.id}”的类型不受支持。`)
  }
  const validEdges: FlowEdge[] = []
  for (const edge of edges) {
    if (!edge.id?.trim()) add(null, '连线缺少 ID。')
    else if (edgeIds.has(edge.id)) add(edge.id, `连线 ID“${edge.id}”重复。`)
    edgeIds.add(edge.id)
    if (!nodeMap.has(edge.sourceId)) add(edge.id, `连线“${edge.id}”的起点不存在。`)
    if (!nodeMap.has(edge.targetId)) add(edge.id, `连线“${edge.id}”的终点不存在。`)
    if (edge.sourceId === edge.targetId && nodeMap.has(edge.sourceId)) add(edge.id, `连线“${edge.id}”不能直接连回自身节点。`)
    if (nodeMap.has(edge.sourceId) && nodeMap.has(edge.targetId)) {
      validEdges.push(edge)
      outgoing.get(edge.sourceId)!.push(edge.targetId)
      incoming.get(edge.targetId)!.push(edge.sourceId)
    }
  }
  const starts = [...nodeMap.values()].filter(node => node.type === flowNodeTypes.Start)
  const ends = [...nodeMap.values()].filter(node => node.type === flowNodeTypes.End)
  if (starts.length !== 1) add(null, '流程必须有且只有一个开始节点。')
  if (ends.length !== 1) add(null, '流程必须有且只有一个结束节点。')
  for (const start of starts) if ((incoming.get(start.id)?.length ?? 0) !== 0 || (outgoing.get(start.id)?.length ?? 0) !== 1) add(start.id, `开始节点“${start.id}”必须没有入线且只有一条出线。`)
  for (const end of ends) if ((outgoing.get(end.id)?.length ?? 0) !== 0 || (incoming.get(end.id)?.length ?? 0) === 0) add(end.id, `结束节点“${end.id}”不能有出线，且至少有一条入线。`)
  for (const node of nodeMap.values()) {
    if (node.type !== flowNodeTypes.Start && node.type !== flowNodeTypes.End && !gateways.has(node.type) && ((incoming.get(node.id)?.length ?? 0) === 0 || (outgoing.get(node.id)?.length ?? 0) !== 1)) add(node.id, `节点“${node.id}”必须至少有一条入线且只有一条出线；分支请使用网关。`)
    if (starts.length === 1 && !traverse(starts[0]!.id, outgoing).has(node.id)) add(node.id, `节点“${node.id}”无法从开始节点到达。`)
    if (ends.length === 1 && !traverse(ends[0]!.id, incoming).has(node.id)) add(node.id, `节点“${node.id}”无法走到结束节点。`)
  }
  const gatewayNodes = [...nodeMap.values()].filter(node => gateways.has(node.type))
  for (const gateway of gatewayNodes) {
    if (gateway.gatewayDirection !== flowGatewayDirections.Split && gateway.gatewayDirection !== flowGatewayDirections.Join) add(gateway.id, `网关“${gateway.id}”必须指定分叉或汇合方向。`)
    if (new Set<string>([flowNodeTypes.ParallelGateway, flowNodeTypes.InclusiveGateway]).has(gateway.type) && !gateway.gatewayPairId?.trim()) add(gateway.id, `网关“${gateway.id}”必须指定配对 ID。`)
    const inCount = incoming.get(gateway.id)?.length ?? 0
    const outCount = outgoing.get(gateway.id)?.length ?? 0
    if (gateway.gatewayDirection === flowGatewayDirections.Split && (inCount !== 1 || outCount < 2)) add(gateway.id, `分叉网关“${gateway.id}”必须有一条入线和至少两条出线。`)
    if (gateway.gatewayDirection === flowGatewayDirections.Join && (inCount < 2 || outCount !== 1)) add(gateway.id, `汇合网关“${gateway.id}”必须有至少两条入线和一条出线。`)
    if (gateway.gatewayDirection === flowGatewayDirections.Split && new Set<string>([flowNodeTypes.ExclusiveGateway, flowNodeTypes.InclusiveGateway]).has(gateway.type)) {
      const branches = validEdges.filter(edge => edge.sourceId === gateway.id)
      if (branches.filter(edge => edge.isDefault).length !== 1) add(gateway.id, `条件网关“${gateway.id}”必须恰好有一条兜底连线。`)
      for (const edge of branches) {
        if (!edge.isDefault && !edge.condition?.trim()) add(edge.id, `条件连线“${edge.id}”缺少条件。`)
        if (edge.isDefault && edge.condition?.trim()) add(edge.id, `兜底连线“${edge.id}”不能设置条件。`)
      }
    }
  }
  const pairs = new Set(gatewayNodes.map(node => node.gatewayPairId).filter((id): id is string => !!id?.trim()))
  for (const pairId of pairs) {
    const members = gatewayNodes.filter(node => node.gatewayPairId === pairId)
    const split = members.find(node => node.gatewayDirection === flowGatewayDirections.Split)
    const join = members.find(node => node.gatewayDirection === flowGatewayDirections.Join)
    if (members.length !== 2 || !split || !join || split.type !== join.type) {
      members.forEach(node => add(node.id, `网关配对“${pairId}”必须恰好包含同类型的一处分叉和一处汇合。`))
      continue
    }
    for (const edge of validEdges.filter(item => item.sourceId === split.id)) {
      if (!traverse(edge.targetId, outgoing).has(join.id)) add(edge.id, `分支“${edge.id}”无法到达配对汇合网关“${join.id}”。`)
      if (new Set<string>([flowNodeTypes.ParallelGateway, flowNodeTypes.InclusiveGateway]).has(split.type) && ends.some(end => traverse(edge.targetId, outgoing, join.id).has(end.id))) add(edge.id, `分支“${edge.id}”可以绕过汇合网关“${join.id}”到达结束节点。`)
    }
  }
  const paths = new Set<string>()
  collectFields(definition.data?.fields ?? [], issues, '', paths)
  for (const edge of edges) checkReferences(edge.condition, edge.id, paths, issues)
  const visitFormula = (fields: FlowFieldDefinition[]): void => { fields.forEach(field => { checkReferences(field.formula, field.key, paths, issues); visitFormula(field.children ?? []) }) }
  visitFormula(definition.data?.fields ?? [])
  for (const node of nodes) {
    if (new Set<string>([flowNodeTypes.Approval, flowNodeTypes.Handling, flowNodeTypes.Cc]).has(node.type) && !node.assignee?.source?.trim()) add(node.id, `人工节点“${node.id}”必须配置处理人来源。`)
    if (node.assignee?.fieldKey?.trim() && !paths.has(node.assignee.fieldKey)) add(node.id, `节点“${node.id}”的处理人字段“${node.assignee.fieldKey}”不存在。`)
    for (const permission of node.fieldPermissions ?? []) if (!paths.has(permission.fieldKey)) add(node.id, `节点“${node.id}”的字段权限引用了不存在的字段“${permission.fieldKey}”。`)
  }
  for (const key of definition.data?.summaryFields ?? []) if (!paths.has(key)) add(key, `摘要字段“${key}”不存在。`)
  return issues
}
