/**
 * 自由画布的纯图操作；所有变更返回新模型，便于工作台保存、撤销和测试。
 */
import { flowNodeTypes } from '../../model/flow.ts'
import type { FlowCanvas, FlowDefinition, FlowEdge, FlowNode, FlowNodeType } from '../../model/flow.ts'

/**
 * 画布网格间距，坐标始终存模型坐标而不是屏幕像素。
 */
export const canvasGrid = 16

/**
 * 复制完整流程定义；Vue 的响应式代理不能直接交给 structuredClone。
 * @param flow 当前流程定义。
 * @returns 与响应式代理解除关联的副本。
 */
export function copyCanvasFlow(flow: FlowDefinition): FlowDefinition {
  return JSON.parse(JSON.stringify(flow)) as FlowDefinition
}

/**
 * 节点工具箱的显示顺序和默认名称。
 */
export const canvasPalette: ReadonlyArray<{ type: FlowNodeType; name: string; group: string }> = [
  { type: flowNodeTypes.Start, name: '开始', group: '事件' },
  { type: flowNodeTypes.End, name: '结束', group: '事件' },
  { type: flowNodeTypes.Approval, name: '审批人', group: '人工节点' },
  { type: flowNodeTypes.Handling, name: '办理人', group: '人工节点' },
  { type: flowNodeTypes.Cc, name: '抄送人', group: '人工节点' },
  { type: flowNodeTypes.ExclusiveGateway, name: '排他网关', group: '网关' },
  { type: flowNodeTypes.InclusiveGateway, name: '包容网关', group: '网关' },
  { type: flowNodeTypes.ParallelGateway, name: '并行网关', group: '网关' },
  { type: flowNodeTypes.Subprocess, name: '子流程', group: '系统节点' },
  { type: flowNodeTypes.Automatic, name: '自动节点', group: '系统节点' },
]

/**
 * 返回最近的网格坐标。
 * @param value 原始坐标。
 * @returns 吸附后的坐标。
 */
export function snapCoordinate(value: number): number {
  return Math.round(value / canvasGrid) * canvasGrid
}

/**
 * 在当前元素集合中生成稳定且不重复的本地 ID。
 * @param prefix 元素类型前缀。
 * @param ids 已有 ID。
 * @returns 新 ID。
 */
export function nextCanvasId(prefix: string, ids: ReadonlyArray<string>): string {
  const occupied = new Set(ids)
  let index = 1
  while (occupied.has(`${prefix}_${index}`)) index++
  return `${prefix}_${index}`
}

/**
 * 根据种类创建具有契约所需最小属性的节点。
 * @param type 节点种类。
 * @param id 新节点 ID。
 * @param x 横坐标。
 * @param y 纵坐标。
 * @returns 可编辑节点。
 */
export function createCanvasNode(type: FlowNodeType, id: string, x: number, y: number): FlowNode {
  const name = canvasPalette.find(item => item.type === type)?.name ?? '节点'
  const node: FlowNode = { id, type, name, x: snapCoordinate(x), y: snapCoordinate(y), fieldPermissions: [] }
  if (new Set<string>([flowNodeTypes.Approval, flowNodeTypes.Handling, flowNodeTypes.Cc]).has(type)) {
    node.assignee = { source: 'initiator' }
  }
  if (new Set<string>([flowNodeTypes.ExclusiveGateway, flowNodeTypes.InclusiveGateway, flowNodeTypes.ParallelGateway]).has(type)) {
    node.gatewayDirection = 'split'
  }
  if (type === flowNodeTypes.Subprocess) node.subprocess = { flowCode: '', waitForCompletion: true, dataMappings: {} }
  if (type === flowNodeTypes.Automatic) node.automation = { actionCode: '', escalateToAdministrator: true }
  return node
}

/**
 * 在画布中增加节点，不改变传入的模型。
 * @param canvas 当前画布。
 * @param node 新节点。
 * @returns 更新后的画布。
 */
export function addCanvasNode(canvas: FlowCanvas, node: FlowNode): FlowCanvas {
  if (canvas.nodes.some(item => item.id === node.id)) throw new Error('节点 ID 已存在。')
  return { nodes: [...canvas.nodes, node], edges: [...canvas.edges] }
}

/**
 * 移动节点并吸附到网格。
 * @param canvas 当前画布。
 * @param id 节点 ID。
 * @param x 横坐标。
 * @param y 纵坐标。
 * @returns 更新后的画布。
 */
export function moveCanvasNode(canvas: FlowCanvas, id: string, x: number, y: number): FlowCanvas {
  return { nodes: canvas.nodes.map(node => node.id === id ? { ...node, x: snapCoordinate(x), y: snapCoordinate(y) } : node), edges: [...canvas.edges] }
}

/**
 * 创建任意有向连线，允许回路和跨分支连线，但不允许直接自连。
 * @param canvas 当前画布。
 * @param sourceId 起点 ID。
 * @param targetId 终点 ID。
 * @returns 更新后的画布和新线 ID。
 */
export function connectCanvasNodes(canvas: FlowCanvas, sourceId: string, targetId: string): { canvas: FlowCanvas; edgeId: string } {
  if (sourceId === targetId) throw new Error('连线不能直接连回自身节点。')
  if (!canvas.nodes.some(node => node.id === sourceId) || !canvas.nodes.some(node => node.id === targetId)) throw new Error('连线端点不存在。')
  if (canvas.edges.some(edge => edge.sourceId === sourceId && edge.targetId === targetId)) throw new Error('这两个节点之间已有同向连线。')
  const edgeId = nextCanvasId('edge', canvas.edges.map(edge => edge.id))
  const priority = canvas.edges.filter(edge => edge.sourceId === sourceId).length
  const edge: FlowEdge = { id: edgeId, sourceId, targetId, priority, isDefault: false }
  return { canvas: { nodes: [...canvas.nodes], edges: [...canvas.edges, edge] }, edgeId }
}

/**
 * 删除节点及其相连的线，或只删除一条线。
 * @param canvas 当前画布。
 * @param id 节点或连线 ID。
 * @returns 更新后的画布。
 */
export function removeCanvasElement(canvas: FlowCanvas, id: string): FlowCanvas {
  return {
    nodes: canvas.nodes.filter(node => node.id !== id),
    edges: canvas.edges.filter(edge => edge.id !== id && edge.sourceId !== id && edge.targetId !== id),
  }
}

/**
 * 更新连线条件和兜底状态；同一出线组只保留一条兜底线。
 * @param canvas 当前画布。
 * @param edgeId 连线 ID。
 * @param condition 条件表达式。
 * @param isDefault 是否为兜底线。
 * @param priority 分支顺序。
 * @returns 更新后的画布。
 */
export function updateCanvasEdge(canvas: FlowCanvas, edgeId: string, condition: string, isDefault: boolean, priority: number): FlowCanvas {
  const current = canvas.edges.find(edge => edge.id === edgeId)
  if (!current) return canvas
  return {
    nodes: [...canvas.nodes],
    edges: canvas.edges.map(edge => {
      if (edge.id === edgeId) return { ...edge, condition: isDefault ? null : condition.trim() || null, isDefault, priority: Math.max(0, Math.trunc(priority)) }
      if (isDefault && edge.sourceId === current.sourceId && edge.isDefault) return { ...edge, isDefault: false }
      return edge
    }),
  }
}

/**
 * 求画布元素包围盒，适配视图时保留留白。
 * @param canvas 当前画布。
 * @returns 包围盒。
 */
export function canvasBounds(canvas: FlowCanvas): { x: number; y: number; width: number; height: number } {
  if (canvas.nodes.length === 0) return { x: 0, y: 0, width: 800, height: 480 }
  const xs = canvas.nodes.map(node => node.x)
  const ys = canvas.nodes.map(node => node.y)
  const x = Math.min(...xs) - 110
  const y = Math.min(...ys) - 90
  return { x, y, width: Math.max(360, Math.max(...xs) - x + 110), height: Math.max(240, Math.max(...ys) - y + 90) }
}
