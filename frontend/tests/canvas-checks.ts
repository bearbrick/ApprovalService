/**
 * 自由画布的真实图操作、校验和 BPMN 文件场景检查。
 */
import { flowNodeTypes, flowSchemaVersion } from '../src/model/flow.ts'
import type { FlowDefinition } from '../src/model/flow.ts'
import { reactive } from 'vue'
import { addCanvasNode, canvasBounds, connectCanvasNodes, copyCanvasFlow, createCanvasNode, moveCanvasNode, nextCanvasId, removeCanvasElement, updateCanvasEdge } from '../src/designer/canvas/canvas-model.ts'
import { bpmnTypeFor, canvasFromBpmn, flowTypeFor } from '../src/designer/canvas/bpmn-modeler-adapter.ts'
import { validateCanvasDefinition } from '../src/designer/canvas/canvas-validation.ts'
import { exportBpmn } from '../src/designer/canvas/bpmn-codec.ts'
import { parseStaticExpression } from '../src/designer/canvas/expression-static.ts'

/**
 * 一条完整的简单顺序审批，全部属性使用正式契约。
 */
const simpleFlow: FlowDefinition = {
  schemaVersion: flowSchemaVersion,
  code: 'SIMPLE_APPROVAL',
  name: '简单审批',
  canvas: {
    nodes: [createCanvasNode(flowNodeTypes.Start, 'start', 80, 160), createCanvasNode(flowNodeTypes.Approval, 'manager', 300, 160), createCanvasNode(flowNodeTypes.End, 'end', 520, 160)],
    edges: [
      { id: 'edge_1', sourceId: 'start', targetId: 'manager', priority: 0, isDefault: false },
      { id: 'edge_2', sourceId: 'manager', targetId: 'end', priority: 0, isDefault: false },
    ],
  },
  data: { fields: [], summaryFields: [] },
  detailLayout: { mode: 'auto', groups: [] },
  printSettings: { orientation: 'portrait', fieldKeys: [], showComments: true, showSignatures: true, includeReceiptPages: false, showLogo: true, showQrCode: true, showWatermark: false },
  allowedClientApps: [],
  allowedInitiatorDepartments: [],
  allowedInitiatorRoles: [],
}

/**
 * 在不依赖浏览器的 Node 检查程序里验证画布核心行为。
 * @param check 记录检查结果的函数。
 */
export async function runCanvasChecks(check: (name: string, value: boolean) => void): Promise<void> {
  const copiedProxy = copyCanvasFlow(reactive(simpleFlow))
  check('画布历史能复制 Vue 响应式流程定义且不保留代理引用', copiedProxy.code === simpleFlow.code && copiedProxy !== simpleFlow && copiedProxy.canvas.nodes !== simpleFlow.canvas.nodes)
  check('完整顺序审批的画布静态校验通过', validateCanvasDefinition(simpleFlow).length === 0)
  const newNode = createCanvasNode(flowNodeTypes.Handling, 'handling', 407, 241)
  const added = addCanvasNode(simpleFlow.canvas, newNode)
  check('十种节点有完整工具箱且新节点吸附到网格', added.nodes.length === 4 && newNode.x === 400 && newNode.y === 240)
  check('节点移动返回新模型，原定义保持不变', moveCanvasNode(added, 'handling', 449, 240).nodes.at(-1)?.x === 448 && simpleFlow.canvas.nodes.length === 3)
  check('新增连线 ID 稳定且允许非相邻节点连接', connectCanvasNodes(added, 'handling', 'end').edgeId === 'edge_3' && nextCanvasId('edge', ['edge_1', 'edge_3']) === 'edge_2')
  const loop = connectCanvasNodes(added, 'handling', 'manager')
  check('回路连线保留原有源节点和目标节点', loop.canvas.edges.some(edge => edge.sourceId === 'handling' && edge.targetId === 'manager'))
  const diagramElements = [
    { id: 'start', type: 'bpmn:StartEvent', x: 62, y: 142, width: 36, height: 36, businessObject: { name: '业务发起' } },
    { id: 'manager', type: 'bpmn:UserTask', x: 250, y: 120, width: 100, height: 80, businessObject: { name: '直属主管' } },
    { id: 'end', type: 'bpmn:EndEvent', x: 502, y: 142, width: 36, height: 36 },
  ]
  const diagram = canvasFromBpmn([...diagramElements, { id: 'edge_1', type: 'bpmn:SequenceFlow', source: diagramElements[0]!, target: diagramElements[1]! }, { id: 'edge_2', type: 'bpmn:SequenceFlow', source: diagramElements[1]!, target: diagramElements[2]! }], simpleFlow.canvas, new Map())
  check('BPMN 图元恢复节点中心坐标并保留审批配置', diagram.nodes[1]?.x === 300 && diagram.nodes[1]?.y === 160 && diagram.nodes[1]?.name === '直属主管' && diagram.nodes[1]?.assignee?.source === 'initiator')
  check('BPMN 顺序流保留原 ID、方向及条件元数据', diagram.edges.length === 2 && diagram.edges[0]?.id === 'edge_1' && diagram.edges[1]?.sourceId === 'manager')
  const handlingDiagram = canvasFromBpmn([...diagramElements, { id: 'newTask', type: 'bpmn:UserTask', x: 600, y: 120, width: 100, height: 80 }], simpleFlow.canvas, new Map([['newTask', flowNodeTypes.Handling]]))
  check('新建 BPMN 人工节点可映射到办理人且生成默认审批配置', handlingDiagram.nodes.find(node => node.id === 'newTask')?.type === 'handling' && handlingDiagram.nodes.find(node => node.id === 'newTask')?.assignee?.source === 'initiator')
  check('标准 BPMN 类型覆盖全部十种受支持的审批节点', Object.values(flowNodeTypes).every(type => !!bpmnTypeFor(type)) && flowTypeFor('bpmn:TimerEvent') === null)
  check('不能直接自连或连接不存在的节点', ['handling', 'missing'].every(target => { try { connectCanvasNodes(added, 'handling', target); return false } catch { return true } }))
  const conditioned = updateCanvasEdge(simpleFlow.canvas, 'edge_1', '${amount} > 50000', false, 2)
  check('连线条件和优先级可修改，发布校验定位不存在字段', conditioned.edges[0]?.priority === 2 && validateCanvasDefinition({ ...simpleFlow, canvas: conditioned }).some(issue => issue.elementId === 'edge_1' && issue.message.includes('amount')))
  const withCondition = (condition: string): FlowDefinition => ({ ...simpleFlow, canvas: { ...simpleFlow.canvas, edges: [{ ...simpleFlow.canvas.edges[0]!, condition }, simpleFlow.canvas.edges[1]!] } })
  check('受限表达式支持字段引用、函数及中文字段路径', parseStaticExpression('IF(${金额} > 50000, SUM(${items.amount}), 0)').references.join(',') === '金额,items.amount')
  check('发布预检拒绝未知函数和函数参数数量错误', ['EXEC("bad")', 'IF(${amount})', 'SUM()'].every(expression => validateCanvasDefinition(withCondition(expression)).some(issue => issue.elementId === 'edge_1' && issue.message.includes('无效'))))
  check('发布预检拒绝纯常量除零但遵循 IF 短路', validateCanvasDefinition(withCondition('1 / 0')).some(issue => issue.message.includes('除数不能为零')) && validateCanvasDefinition(withCondition('IF(TRUE, 1, 1 / 0)')).length === 0)
  check('人民币大写参与常量条件时仍检查实际执行分支', validateCanvasDefinition(withCondition("IF(RMBUPPER(0) == '零元整', 1 / 0, 1)")).some(issue => issue.message.includes('除数不能为零')))
  check('发布预检拒绝超长与深嵌套表达式', ['1'.repeat(2049), '('.repeat(33) + '1' + ')'.repeat(33)].every(expression => validateCanvasDefinition(withCondition(expression)).some(issue => issue.message.includes('无效'))))
  const defaulted = updateCanvasEdge(conditioned, 'edge_1', '${amount} > 50000', true, 2)
  check('兜底线会清理条件，删除节点同时删除关联连线', defaulted.edges[0]?.condition === null && removeCanvasElement(simpleFlow.canvas, 'manager').edges.length === 0)
  check('缺少结束路径和错误网关可定位到对应元素', validateCanvasDefinition({ ...simpleFlow, canvas: { nodes: [...simpleFlow.canvas.nodes, createCanvasNode(flowNodeTypes.ParallelGateway, 'split', 700, 160)], edges: [...simpleFlow.canvas.edges] } }).some(issue => issue.elementId === 'split'))
  const bounds = canvasBounds(simpleFlow.canvas)
  check('适配范围沿用审批节点中心坐标', bounds.width >= 360)
  const xml = await exportBpmn(simpleFlow)
  check('BPMN 导出包含标准节点、顺序流和带 SHA 的本服务扩展', xml.includes('bpmn:startEvent') && xml.includes('bpmn:sequenceFlow') && xml.includes('approval:flowDefinition') && /sha256="[0-9a-f]{64}"/.test(xml))
  const configured: FlowDefinition = {
    ...simpleFlow,
    withdrawBeforeFirstDecision: true,
    returnMode: 'returnNode',
    data: { fields: [{ key: 'amount', name: '金额', type: 'number', required: true, approverInput: false, businessUpdateAllowed: true, children: [] }], summaryFields: ['amount'] },
    canvas: {
      ...simpleFlow.canvas,
      nodes: simpleFlow.canvas.nodes.map(node => node.id === 'manager' ? {
        ...node,
        assignee: { source: 'role', value: 'FINANCE', limitToInitiatingDepartment: true, stopAtGrade: 'DIRECTOR' },
        approval: { ...node.approval!, missingAssigneeBehavior: 'specified', missingAssigneeUserId: 'user-123' },
      } : node),
    },
  }
  const configuredXml = await exportBpmn(configured)
  const encoded = configuredXml.match(/<approval:flowDefinition[^>]*>([^<]+)<\/approval:flowDefinition>/)?.[1] ?? ''
  const exported = JSON.parse(new TextDecoder().decode(Uint8Array.from(atob(encoded), char => char.charCodeAt(0)))) as FlowDefinition
  const exportedNode = exported.canvas.nodes.find(node => node.id === 'manager')
  check('BPMN 扩展保留找人兜底、部门限制和截止职级', exportedNode?.assignee?.limitToInitiatingDepartment === true && exportedNode.assignee.stopAtGrade === 'DIRECTOR' && exportedNode.approval?.missingAssigneeUserId === 'user-123')
  check('BPMN 扩展保留流程撤回与退回重提策略', exported.withdrawBeforeFirstDecision === true && exported.returnMode === 'returnNode')
  check('BPMN 扩展保留业务系统审批中更新字段的授权', exported.data.fields[0]?.businessUpdateAllowed === true)
}
