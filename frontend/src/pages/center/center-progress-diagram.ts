import type { CenterProgressGraph } from '../../model/center.ts'

/**
 * BPMN 图元布局需要的尺寸及名称。
 */
interface ShapeLayout {
  /**
   * BPMN 标准元素名。
   */
  tag: string
  /**
   * 图形宽度。
   */
  width: number
  /**
   * 图形高度。
   */
  height: number
}

/**
 * 将审批节点类型映射到标准 BPMN 形状。
 * @param type 审批服务节点类型。
 * @returns 标准形状和尺寸。
 */
function shape(type: string): ShapeLayout {
  if (type === 'start') return { tag: 'startEvent', width: 36, height: 36 }
  if (type === 'end') return { tag: 'endEvent', width: 36, height: 36 }
  if (type === 'exclusiveGateway') return { tag: 'exclusiveGateway', width: 50, height: 50 }
  if (type === 'inclusiveGateway') return { tag: 'inclusiveGateway', width: 50, height: 50 }
  if (type === 'parallelGateway') return { tag: 'parallelGateway', width: 50, height: 50 }
  if (type === 'automatic') return { tag: 'serviceTask', width: 100, height: 80 }
  if (type === 'cc') return { tag: 'sendTask', width: 100, height: 80 }
  if (type === 'subprocess') return { tag: 'callActivity', width: 100, height: 80 }
  return { tag: 'userTask', width: 100, height: 80 }
}

/**
 * 安全编码 XML 属性和文字，不解释服务端节点名称。
 * @param value 原始文字。
 * @returns XML 安全文字。
 */
function escapeXml(value: string): string {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;').replaceAll("'", '&apos;')
}

/**
 * 为只读图构造标准 BPMN 2.0 XML 和 DI 布局，图形由 bpmn-js 绘制。
 * @param graph 服务端安全裁剪的已到达路径。
 * @returns 可交给 bpmn-js Viewer 的 XML。
 */
export function centerProgressXml(graph: CenterProgressGraph): string {
  const ids = new Map(graph.nodes.map((node, index) => [node.id, `Node_${index + 1}`]))
  const positions = new Map(graph.nodes.map((node, index) => [node.id, {
    x: Number.isFinite(node.x) ? node.x : index * 180,
    y: Number.isFinite(node.y) ? node.y : 0,
  }]))
  const modelNodes = graph.nodes.map(node => {
    const id = ids.get(node.id)
    if (!id) return ''
    return `<bpmn:${shape(node.type).tag} id="${id}" name="${escapeXml(node.name)}" />`
  }).join('')
  const modelEdges = graph.edges.map((edge, index) => {
    const from = ids.get(edge.sourceId)
    const to = ids.get(edge.targetId)
    return from && to ? `<bpmn:sequenceFlow id="Flow_${index + 1}" sourceRef="${from}" targetRef="${to}" />` : ''
  }).join('')
  const diagramNodes = graph.nodes.map(node => {
    const id = ids.get(node.id)
    const point = positions.get(node.id)
    if (!id || !point) return ''
    const layout = shape(node.type)
    return `<bpmndi:BPMNShape id="Shape_${id}" bpmnElement="${id}"><dc:Bounds x="${point.x - layout.width / 2}" y="${point.y - layout.height / 2}" width="${layout.width}" height="${layout.height}" /></bpmndi:BPMNShape>`
  }).join('')
  const diagramEdges = graph.edges.map((edge, index) => {
    const source = graph.nodes.find(node => node.id === edge.sourceId)
    const target = graph.nodes.find(node => node.id === edge.targetId)
    const from = positions.get(edge.sourceId)
    const to = positions.get(edge.targetId)
    if (!source || !target || !from || !to) return ''
    const sx = from.x + shape(source.type).width / 2
    const tx = to.x - shape(target.type).width / 2
    const horizontal = tx >= sx + 18
    const waypoints = horizontal
      ? [{ x: sx, y: from.y }, { x: (sx + tx) / 2, y: from.y },
        { x: (sx + tx) / 2, y: to.y }, { x: tx, y: to.y }]
      : [{ x: from.x, y: from.y + shape(source.type).height / 2 },
        { x: from.x, y: Math.max(from.y, to.y) + 80 },
        { x: to.x, y: Math.max(from.y, to.y) + 80 },
        { x: to.x, y: to.y + shape(target.type).height / 2 }]
    return `<bpmndi:BPMNEdge id="Edge_${index + 1}" bpmnElement="Flow_${index + 1}">${waypoints.map(point =>
      `<di:waypoint x="${point.x}" y="${point.y}" />`).join('')}</bpmndi:BPMNEdge>`
  }).join('')
  return `<?xml version="1.0" encoding="UTF-8"?><bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" id="Definitions_Progress" targetNamespace="urn:approvalservice:center"><bpmn:process id="Process_Progress" isExecutable="false">${modelNodes}${modelEdges}</bpmn:process><bpmndi:BPMNDiagram id="Diagram_Progress"><bpmndi:BPMNPlane id="Plane_Progress" bpmnElement="Process_Progress">${diagramNodes}${diagramEdges}</bpmndi:BPMNPlane></bpmndi:BPMNDiagram></bpmn:definitions>`
}
