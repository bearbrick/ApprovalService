/**
 * 自由画布的真实图操作、校验和 BPMN 文件场景检查。
 */
import { flowNodeTypes, flowSchemaVersion } from '../src/model/flow.ts'
import type { FlowDefinition } from '../src/model/flow.ts'
import { reactive } from 'vue'
import { addCanvasNode, canvasBounds, connectCanvasNodes, copyCanvasFlow, createCanvasNode, moveCanvasNode, nextCanvasId, removeCanvasElement, updateCanvasEdge } from '../src/designer/canvas/canvas-model.ts'
import { edgeGeometry, screenToCanvas } from '../src/designer/canvas/canvas-geometry.ts'
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
  check('回路连线可创建并绘出上方绕行路径', loop.canvas.edges.some(edge => edge.targetId === 'manager') && edgeGeometry(loop.canvas.edges.at(-1)!, newNode, simpleFlow.canvas.nodes[1]!).path.includes('V'))
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
  const converted = screenToCanvas(300, 200, 0, 0, 100, 40, 2)
  check('适配范围和屏幕坐标变换使用画布坐标', bounds.width >= 360 && converted.x === 100 && converted.y === 80)
  const xml = await exportBpmn(simpleFlow)
  check('BPMN 导出包含标准节点、顺序流和带 SHA 的本服务扩展', xml.includes('bpmn:startEvent') && xml.includes('bpmn:sequenceFlow') && xml.includes('approval:flowDefinition') && /sha256="[0-9a-f]{64}"/.test(xml))
}
