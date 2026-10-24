import { buildCondition } from '../src/designer/workbench/condition-model.ts'
import { flowReturnModes } from '../src/model/flow.ts'
import { fieldReferences } from '../src/designer/data/data-model.ts'
import type { ConditionGroup } from '../src/designer/workbench/condition-model.ts'
import { checkPublication } from '../src/designer/workbench/publication-check.ts'
import { commitDraft, createDraftHistory, editDraft, previewDraft, redoDraft, undoDraft } from '../src/designer/workbench/draft-history.ts'
import { copyFlow, createEmptyFlow, flowTemplates, normalizeSample, replaceEdge, replaceNode } from '../src/designer/workbench/workbench-model.ts'
import { simpleFlowTemplates } from '../src/designer/workbench/simple-templates.ts'
import { buildCSharpStartSnippet } from '../src/designer/workbench/integration-snippet.ts'
import { actionsForNodeType, defaultActionsForNodeType } from '../src/designer/workbench/rule-options.ts'

/**
 * 前端工作台纯逻辑的场景检查。
 * @param check 检查函数。
 */
export function runWorkbenchChecks(check: (name: string, value: boolean) => void): void {
  const blank = createEmptyFlow('DEMO')
  check('新流程默认审批结束前可撤回、退回后从头重提且不保密', blank.withdrawBeforeFirstDecision === false && blank.returnMode === flowReturnModes.Restart && blank.isConfidential === false)
  const legacy = copyFlow(blank)
  delete legacy.withdrawBeforeFirstDecision
  delete legacy.returnMode
  delete legacy.isConfidential
  const normalized = normalizeSample(legacy)
  check('旧草稿省略流程级策略时恢复安全默认值', normalized.withdrawBeforeFirstDecision === false && normalized.returnMode === flowReturnModes.Restart && normalized.isConfidential === false)
  const configured = copyFlow(blank)
  configured.withdrawBeforeFirstDecision = true
  configured.returnMode = flowReturnModes.ReturnNode
  configured.isConfidential = true
  check('三种流程级策略能保留在独立草稿并通过发布预检', copyFlow(configured).withdrawBeforeFirstDecision === true && copyFlow(configured).returnMode === flowReturnModes.ReturnNode && copyFlow(configured).isConfidential === true && checkPublication(configured).length === 0)
  const invalidMode = JSON.parse(JSON.stringify(blank)) as typeof blank
  invalidMode.returnMode = 'unknown' as typeof invalidMode.returnMode
  check('不支持的退回重提方式被前端发布预检拒绝', checkPublication(invalidMode).some(issue => issue.message.includes('退回后重新提交方式')))
  const invalidConfidential = copyFlow(blank)
  invalidConfidential.isConfidential = 'yes' as unknown as boolean
  check('非布尔保密标记被前端发布预检拒绝', checkPublication(invalidConfidential).some(issue => issue.message.includes('保密流程标记')))
  check('空白自由画布具有唯一开始和结束且能通过结构预检', blank.canvas.nodes.length === 2 && checkPublication(blank).length === 0)
  check('十类正式模板覆盖常见审批且均能通过发布预检', flowTemplates.length === 10 &&
    new Set(flowTemplates.map(template => template.code)).size === 10 &&
    flowTemplates.every(template => checkPublication(template.definition).length === 0))
  check('七类新增通用模板只定义审批图与业务字段，不带业务单据或外部动作', simpleFlowTemplates.length === 7 && simpleFlowTemplates.every(template =>
    template.definition.data.fields.length > 0 && template.definition.canvas.nodes.length > 0 &&
    template.definition.canvas.nodes.every(node => node.type !== 'automatic')))
  const snippet = buildCSharpStartSnippet('CONTRACT_REVIEW')
  check('接入示例使用 SDK 和已授权流程，API Key 从环境读取不写进源码', snippet.includes('new ApprovalServiceClient(') &&
    snippet.includes('FlowCode = "CONTRACT_REVIEW"') && snippet.includes('GetEnvironmentVariable("APPROVAL_API_KEY")') &&
    snippet.includes('StartApprovalRequest') && snippet.includes('requestId).ConfigureAwait(false)'))
  const escapedCode = 'X"\\\n'
  check('流程编码经字符串字面量转义，无法注入 C# 语句', buildCSharpStartSnippet(escapedCode).includes(String.raw`FlowCode = "X\"\\\u000a"`))
  check('中文和 Unicode 行分隔符在 C# 源码里按码元转义', buildCSharpStartSnippet('中文\u2028').includes(String.raw`FlowCode = "\u4e2d\u6587\u2028"`))
  let invalidSnippetRejected = false
  try { buildCSharpStartSnippet('   ') } catch { invalidSnippetRejected = true }
  check('接入示例拒绝空流程编码', invalidSnippetRejected)
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
