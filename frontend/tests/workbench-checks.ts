import { buildCondition } from '../src/designer/workbench/condition-model.ts'
import { fieldReferences } from '../src/designer/data/data-model.ts'
import type { ConditionGroup } from '../src/designer/workbench/condition-model.ts'
import { checkPublication } from '../src/designer/workbench/publication-check.ts'
import { commitDraft, createDraftHistory, editDraft, previewDraft, redoDraft, undoDraft } from '../src/designer/workbench/draft-history.ts'
import { copyFlow, createEmptyFlow, flowTemplates, replaceEdge, replaceNode } from '../src/designer/workbench/workbench-model.ts'
import { actionsForNodeType, defaultActionsForNodeType } from '../src/designer/workbench/rule-options.ts'

/**
 * 前端工作台纯逻辑的场景检查。
 * @param check 检查函数。
 */
export function runWorkbenchChecks(check: (name: string, value: boolean) => void): void {
  const blank = createEmptyFlow('DEMO')
  check('空白自由画布具有唯一开始和结束且能通过结构预检', blank.canvas.nodes.length === 2 && checkPublication(blank).length === 0)
  check('三个正式示例模板覆盖采购、费用与质量异常且均能通过发布预检', flowTemplates.length === 3 && flowTemplates.every(template => checkPublication(template.definition).length === 0))
  check('正式示例可展开明细字段并显示节点字段权限', flowTemplates.every(template => fieldReferences(template.definition.data).length >= template.definition.data.fields.length))
  check('办理节点默认只能提交且不能配置同意或拒绝', defaultActionsForNodeType('handling').join(',') === 'submit' && !actionsForNodeType('handling').includes('approve') && !actionsForNodeType('handling').includes('reject'))
  check('抄送节点不提供审批操作', defaultActionsForNodeType('cc').length === 0 && actionsForNodeType('cc').length === 0)
  const duplicate = copyFlow(blank)
  duplicate.canvas.nodes.push({ ...duplicate.canvas.nodes[0]! })
  check('发布预检能把重复节点定位到节点 ID', checkPublication(duplicate).some(issue => issue.elementId === 'start' && issue.message.includes('重复')))
  const disconnected = copyFlow(blank)
  disconnected.canvas.edges = []
  check('发布预检发现开始节点缺出口和结束节点不可到达', checkPublication(disconnected).some(issue => issue.elementId === 'end' && issue.message.includes('无法从开始')))
  const conditional = copyFlow(flowTemplates[0]!.definition)
  const gateway = conditional.canvas.nodes.find(node => node.type === 'exclusiveGateway')!
  conditional.canvas.edges.filter(edge => edge.sourceId === gateway.id).forEach(edge => { edge.isDefault = false; edge.condition = '' })
  check('排他网关缺兜底和条件时同时返回网关与连线问题', checkPublication(conditional).some(issue => issue.elementId === gateway.id && issue.message.includes('兜底')) && checkPublication(conditional).some(issue => issue.message.includes('缺少条件')))
  const assignee = copyFlow(blank)
  assignee.canvas.nodes.splice(1, 0, { id: 'review', type: 'approval', name: '审批', x: 250, y: 220, fieldPermissions: [] })
  assignee.canvas.edges[0]!.targetId = 'review'
  assignee.canvas.edges.push({ id: 'review-end', sourceId: 'review', targetId: 'end', priority: 0, isDefault: false })
  check('人工节点缺少处理人来源时报告可定位的发布问题', checkPublication(assignee).some(issue => issue.elementId === 'review' && issue.message.includes('处理人来源')))
  const fieldFlow = copyFlow(blank)
  fieldFlow.data.fields.push({ key: 'amount', name: '金额', type: 'money', required: true, approverInput: false, children: [] })
  fieldFlow.canvas.edges[0]!.condition = '${missing} > 0'
  check('条件引用不存在的数据字段时问题定位到连线', checkPublication(fieldFlow).some(issue => issue.elementId === 'start-end' && issue.message.includes('missing')))
  const original = copyFlow(blank)
  replaceNode(original, { ...original.canvas.nodes[0]!, name: '业务系统发起' }).canvas.nodes[0]!.name = '修改返回值'
  replaceEdge(original, { ...original.canvas.edges[0]!, priority: 3 }).canvas.edges[0]!.priority = 5
  check('节点和连线的替换不会修改原草稿', original.canvas.nodes[0]!.name === '开始' && original.canvas.edges[0]!.priority === 0)
  const groups: ConditionGroup[] = [{ join: 'AND', rules: [{ fieldKey: 'items.amount', aggregate: 'SUM', operator: '>=', value: '50000' }, { fieldKey: 'urgent', aggregate: '', operator: '==', value: 'true' }] }]
  const data = { fields: [{ key: 'items', name: '明细', type: 'detailTable' as const, required: false, approverInput: false, children: [{ key: 'amount', name: '金额', type: 'money' as const, required: false, approverInput: false, children: [] }] }, { key: 'urgent', name: '加急', type: 'text' as const, required: false, approverInput: false, children: [] }], summaryFields: [] }
  check('结构化条件可生成明细合计与且分组表达式', buildCondition(groups, data) === '(SUM(${items.amount}) >= 50000 AND ${urgent} == true)')
  const initial = createEmptyFlow('HISTORY')
  const movedOnce = replaceNode(initial, { ...initial.canvas.nodes[0]!, x: 112 })
  const movedTwice = replaceNode(movedOnce, { ...movedOnce.canvas.nodes[0]!, x: 128 })
  const renamed = replaceNode(movedTwice, { ...movedTwice.canvas.nodes[0]!, name: '业务系统发起' })
  let history = createDraftHistory(initial)
  history = previewDraft(history, movedOnce)
  history = previewDraft(history, movedTwice)
  history = commitDraft(history, initial)
  history = editDraft(history, renamed)
  check('画布拖动多帧与节点属性交错编辑只登记两个撤销步骤', history.past.length === 2 && history.present.canvas.nodes[0]!.name === '业务系统发起')
  history = undoDraft(history)
  check('首次撤销仅还原节点属性而保留拖动位置', history.present.canvas.nodes[0]!.name === '开始' && history.present.canvas.nodes[0]!.x === 128)
  history = undoDraft(history)
  check('再次撤销还原画布拖动且重做依次恢复两次编辑', history.present.canvas.nodes[0]!.x === 80 && redoDraft(redoDraft(history)).present.canvas.nodes[0]!.name === '业务系统发起')
}
