import { flowFieldTypes, flowNodeTypes } from '../../model/flow.ts'
import type { FlowDefinition, FlowEdge, FlowNode } from '../../model/flow.ts'
import type { DesignerIssue } from './workbench-model.ts'

/**
 * 后端发布检查支持的字段类型。
 */
const knownFieldTypes = new Set<string>(Object.values(flowFieldTypes))
/**
 * 后端发布检查支持的节点类型。
 */
const knownNodeTypes = new Set<string>(Object.values(flowNodeTypes))
/**
 * 支持的有限表达式函数名；其他函数不允许在发布时执行。
 */
const knownFunctions = new Set(['SUM', 'AVG', 'MAX', 'MIN', 'IF', 'ANY', 'DATEDIFF', 'RMBUPPER'])

/**
 * 在前端执行与 FlowCompiler 对齐的发布预检，并累积可定位的问题。
 * @param definition 当前完整流程定义。
 * @returns 所有结构、字段与规则问题。
 */
export function checkPublication(definition: FlowDefinition): DesignerIssue[] {
  const issues: DesignerIssue[] = []
  const add = (elementId: string | null, message: string): void => { issues.push({ elementId, message }) }
  if (definition.schemaVersion !== 1) add(null, `流程定义格式版本不受支持：${definition.schemaVersion}。`)
  if (!definition.code.trim()) add(null, '流程编码不能为空。')
  if (!definition.name.trim()) add(null, '流程名称不能为空。')
  const nodes = definition.canvas.nodes
  const edges = definition.canvas.edges
  const nodeById = new Map<string, FlowNode>()
  const edgeIds = new Set<string>()
  for (const node of nodes) {
    if (!node.id.trim()) add(null, '节点缺少 ID。')
    else if (nodeById.has(node.id)) add(node.id, `节点 ID“${node.id}”重复。`)
    else nodeById.set(node.id, node)
    if (!knownNodeTypes.has(node.type)) add(node.id, `节点“${node.id}”的类型不受支持。`)
  }
  const validEdges: FlowEdge[] = []
  for (const edge of edges) {
    if (!edge.id.trim()) add(null, '连线缺少 ID。')
    else if (edgeIds.has(edge.id)) add(edge.id, `连线 ID“${edge.id}”重复。`)
    else edgeIds.add(edge.id)
    if (!nodeById.has(edge.sourceId)) add(edge.id, `连线“${edge.id}”的起点不存在。`)
    if (!nodeById.has(edge.targetId)) add(edge.id, `连线“${edge.id}”的终点不存在。`)
    if (edge.sourceId === edge.targetId && nodeById.has(edge.sourceId)) add(edge.id, `连线“${edge.id}”不能直接连回自身节点。`)
    if (nodeById.has(edge.sourceId) && nodeById.has(edge.targetId)) validEdges.push(edge)
  }
  const incoming = (id: string): FlowEdge[] => validEdges.filter(edge => edge.targetId === id)
  const outgoing = (id: string): FlowEdge[] => validEdges.filter(edge => edge.sourceId === id)
  const starts = nodes.filter(node => node.type === 'start')
  const ends = nodes.filter(node => node.type === 'end')
  if (starts.length !== 1) add(null, '流程必须有且只有一个开始节点。')
  if (ends.length !== 1) add(null, '流程必须有且只有一个结束节点。')
  for (const node of starts) if (incoming(node.id).length || outgoing(node.id).length !== 1) add(node.id, `开始节点“${node.id}”必须没有入线且只有一条出线。`)
  for (const node of ends) if (outgoing(node.id).length || !incoming(node.id).length) add(node.id, `结束节点“${node.id}”不能有出线，且至少有一条入线。`)
  for (const node of nodes.filter(node => node.type !== 'start' && node.type !== 'end' && !isGateway(node))) {
    if (!incoming(node.id).length || outgoing(node.id).length !== 1) add(node.id, `节点“${node.id}”必须至少有一条入线且只有一条出线；分支请使用网关。`)
  }
  const reach = (startId: string, direction: 'forward' | 'backward', excluded = ''): Set<string> => {
    const seen = new Set<string>()
    const pending = [startId]
    while (pending.length) {
      const current = pending.pop()!
      if (current === excluded || seen.has(current)) continue
      seen.add(current)
      for (const edge of direction === 'forward' ? outgoing(current) : incoming(current)) pending.push(direction === 'forward' ? edge.targetId : edge.sourceId)
    }
    return seen
  }
  const fromStart = starts.length === 1 ? reach(starts[0]!.id, 'forward') : null
  const toEnd = ends.length === 1 ? reach(ends[0]!.id, 'backward') : null
  for (const node of nodes) {
    if (fromStart && !fromStart.has(node.id)) add(node.id, `节点“${node.id}”无法从开始节点到达。`)
    if (toEnd && !toEnd.has(node.id)) add(node.id, `节点“${node.id}”无法走到结束节点。`)
  }
  const gateways = nodes.filter(isGateway)
  for (const gateway of gateways) {
    if (gateway.gatewayDirection !== 'split' && gateway.gatewayDirection !== 'join') add(gateway.id, `网关“${gateway.id}”必须指定分叉或汇合方向。`)
    if ((gateway.type === 'parallelGateway' || gateway.type === 'inclusiveGateway') && !gateway.gatewayPairId?.trim()) add(gateway.id, `网关“${gateway.id}”必须指定配对 ID。`)
    if (gateway.gatewayDirection === 'split' && (incoming(gateway.id).length !== 1 || outgoing(gateway.id).length < 2)) add(gateway.id, `分叉网关“${gateway.id}”必须有一条入线和至少两条出线。`)
    if (gateway.gatewayDirection === 'join' && (incoming(gateway.id).length < 2 || outgoing(gateway.id).length !== 1)) add(gateway.id, `汇合网关“${gateway.id}”必须有至少两条入线和一条出线。`)
    if (gateway.gatewayDirection === 'split' && gateway.type !== 'parallelGateway') {
      const branches = outgoing(gateway.id)
      if (branches.filter(edge => edge.isDefault).length !== 1) add(gateway.id, `条件网关“${gateway.id}”必须恰好有一条兜底连线。`)
      for (const edge of branches) {
        if (!edge.isDefault && !edge.condition?.trim()) add(edge.id, `条件连线“${edge.id}”缺少条件。`)
        if (edge.isDefault && edge.condition?.trim()) add(edge.id, `兜底连线“${edge.id}”不能设置条件。`)
      }
    }
  }
  const groups = new Map<string, FlowNode[]>()
  for (const gateway of gateways) if (gateway.gatewayPairId?.trim()) groups.set(gateway.gatewayPairId, [...(groups.get(gateway.gatewayPairId) ?? []), gateway])
  for (const [pairId, members] of groups) {
    const splits = members.filter(node => node.gatewayDirection === 'split')
    const joins = members.filter(node => node.gatewayDirection === 'join')
    if (members.length !== 2 || splits.length !== 1 || joins.length !== 1 || splits[0]!.type !== joins[0]!.type) {
      for (const member of members) add(member.id, `网关配对“${pairId}”必须恰好包含同类型的一处分叉和一处汇合。`)
      continue
    }
    const split = splits[0]!
    const join = joins[0]!
    for (const edge of outgoing(split.id)) {
      if (!reach(edge.targetId, 'forward').has(join.id)) add(edge.id, `分支“${edge.id}”无法到达配对汇合网关“${join.id}”。`)
      if (split.type !== 'exclusiveGateway' && ends.some(end => reach(edge.targetId, 'forward', join.id).has(end.id))) add(edge.id, `分支“${edge.id}”可以绕过汇合网关“${join.id}”到达结束节点。`)
    }
  }
  const fields = collectFields(definition, add)
  for (const edge of edges) checkExpression(edge.condition, edge.id, fields, add)
  for (const field of definition.data.fields) {
    checkExpression(field.formula, field.key, fields, add)
    for (const child of field.children ?? []) checkExpression(child.formula, child.key, fields, add)
  }
  for (const node of nodes) {
    if (['approval', 'handling', 'cc'].includes(node.type) && !node.assignee?.source?.trim()) add(node.id, `人工节点“${node.id}”必须配置处理人来源。`)
    if (node.assignee?.fieldKey && !fields.has(node.assignee.fieldKey)) add(node.id, `节点“${node.id}”的处理人字段“${node.assignee.fieldKey}”不存在。`)
    for (const permission of node.fieldPermissions ?? []) if (!fields.has(permission.fieldKey)) add(node.id, `节点“${node.id}”的字段权限引用了不存在的字段“${permission.fieldKey}”。`)
  }
  for (const summary of definition.data.summaryFields) if (!fields.has(summary)) add(summary, `摘要字段“${summary}”不存在。`)
  return issues
}

/**
 * 判断一个节点是否为网关。
 * @param node 画布节点。
 * @returns 网关节点返回真。
 */
function isGateway(node: FlowNode): boolean {
  return node.type === 'exclusiveGateway' || node.type === 'inclusiveGateway' || node.type === 'parallelGateway'
}

/**
 * 收集审批数据的所有可引用字段路径，并检查重复和明细结构。
 * @param definition 流程定义。
 * @param add 报告问题的回调。
 * @returns 字段路径集合。
 */
function collectFields(definition: FlowDefinition, add: (id: string | null, message: string) => void): Set<string> {
  const keys = new Set<string>()
  const visit = (items: FlowDefinition['data']['fields'], prefix: string): void => {
    for (const field of items) {
      if (!field.key.trim()) { add(prefix || null, '审批数据字段缺少 key。'); continue }
      const path = prefix ? `${prefix}.${field.key}` : field.key
      if (keys.has(path)) add(path, `审批数据字段“${path}”重复。`)
      keys.add(path)
      if (!knownFieldTypes.has(field.type)) add(path, `审批数据字段“${path}”的类型不受支持。`)
      if (field.type !== 'detailTable' && field.children?.length) add(path, `非明细表字段“${path}”不能包含子字段。`)
      if (field.type === 'detailTable' && !field.children?.length) add(path, `明细表字段“${path}”至少要有一个子字段。`)
      visit(field.children ?? [], path)
    }
  }
  visit(definition.data.fields, '')
  return keys
}

/**
 * 检查表达式中的字段引用和未支持函数；完整求值仍交给服务端。
 * @param expression 条件或计算表达式。
 * @param elementId 连线或字段 ID。
 * @param fields 已定义的字段路径。
 * @param add 报告问题的回调。
 */
function checkExpression(expression: string | null | undefined, elementId: string, fields: Set<string>, add: (id: string | null, message: string) => void): void {
  if (!expression?.trim()) return
  if (expression.length > 2048) add(elementId, `表达式“${elementId}”超过 2048 字符。`)
  for (const match of expression.matchAll(/\$\{([^}]*)\}/g)) {
    const key = match[1]!
    if (!fields.has(key) && !['initiator.department', 'initiator.role', 'initiator.level'].includes(key)) add(elementId, `表达式“${elementId}”引用了不存在的字段“${key}”。`)
  }
  const withoutStrings = expression.replace(/(['"])(?:\\.|(?!\1).)*?\1/g, '')
  for (const match of withoutStrings.matchAll(/\b([A-Za-z][A-Za-z0-9_]*)\s*\(/g)) if (!knownFunctions.has(match[1]!.toUpperCase())) add(elementId, `表达式“${elementId}”使用了不支持的函数“${match[1]}”。`)
  if (/\/\s*0(?:\.0+)?(?:\D|$)/.test(withoutStrings)) add(elementId, `表达式“${elementId}”包含显式除零。`)
}
