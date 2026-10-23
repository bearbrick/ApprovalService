/**
 * SVG 连线几何与视图变换，不依赖浏览器，供画布交互和检查程序共用。
 */
import type { FlowEdge, FlowNode } from '../../model/flow.ts'

/**
 * 普通节点的交互半宽。
 */
export const nodeHalfWidth = 76
/**
 * 普通节点的交互半高。
 */
export const nodeHalfHeight = 29

/**
 * 画布上的一个坐标。
 */
export interface CanvasPoint {
  /**
   * 横坐标。
   */
  x: number
  /**
   * 纵坐标。
   */
  y: number
}

/**
 * 一条线的 SVG 绘制结果。
 */
export interface EdgeGeometry {
  /**
   * 主线条的 SVG path d。
   */
  path: string
  /**
   * 折线各端点，便于检查避让结果。
   */
  points: ReadonlyArray<CanvasPoint>
  /**
   * 条件标签横坐标。
   */
  labelX: number
  /**
   * 条件标签纵坐标。
   */
  labelY: number
}

/**
 * 节点图形的外接尺寸。
 * @param node 节点。
 * @returns 半宽与半高。
 */
function nodeSize(node: FlowNode): { width: number; height: number } {
  if (node.type === 'start' || node.type === 'end') return { width: 25, height: 25 }
  if (node.type.endsWith('Gateway')) return { width: 42, height: 35 }
  return { width: nodeHalfWidth, height: nodeHalfHeight }
}

/**
 * 同一侧的线按另一端的纵坐标排序，分配互不重叠的端口。
 * @param edge 当前连线。
 * @param node 当前节点。
 * @param edges 全部连线。
 * @param nodes 全部节点。
 * @param outgoing 是否计算出线。
 * @returns 相对于节点中心的纵向偏移。
 */
function portOffset(edge: FlowEdge, node: FlowNode, edges: ReadonlyArray<FlowEdge>, nodes: ReadonlyArray<FlowNode>, outgoing: boolean): number {
  const peers = edges.filter(item => outgoing ? item.sourceId === node.id : item.targetId === node.id)
  if (peers.length < 2) return 0
  const byId = new Map(nodes.map(item => [item.id, item]))
  peers.sort((first, second) => {
    const a = byId.get(outgoing ? first.targetId : first.sourceId)
    const b = byId.get(outgoing ? second.targetId : second.sourceId)
    return (a?.y ?? 0) - (b?.y ?? 0) || (a?.x ?? 0) - (b?.x ?? 0) || first.id.localeCompare(second.id)
  })
  const index = peers.findIndex(item => item.id === edge.id)
  const height = nodeSize(node).height
  const spacing = Math.min(16, height * 1.25 / (peers.length - 1))
  return Math.round((index - (peers.length - 1) / 2) * spacing)
}

/**
 * 计算矩形、圆形或菱形节点真实边缘上的端口。
 * @param node 节点。
 * @param offset 端口纵向偏移。
 * @param right 是否取右侧端口。
 * @returns 节点边缘上的坐标。
 */
function sidePort(node: FlowNode, offset: number, right: boolean): CanvasPoint {
  const { width, height } = nodeSize(node)
  let distance = width
  if (node.type === 'start' || node.type === 'end') distance = Math.sqrt(width * width - offset * offset)
  if (node.type.endsWith('Gateway')) distance = width * (1 - Math.abs(offset) / height)
  return { x: Math.round(node.x + (right ? distance : -distance)), y: node.y + offset }
}

/**
 * 判断折线是否穿过其他节点预留的安全区域。
 * @param points 折线端点。
 * @param nodes 当前画布节点。
 * @param sourceId 起点 ID。
 * @param targetId 终点 ID。
 * @returns 有碰撞时为真。
 */
function intersectsNode(points: ReadonlyArray<CanvasPoint>, nodes: ReadonlyArray<FlowNode>, sourceId: string, targetId: string): boolean {
  return nodes.some(node => {
    if (node.id === sourceId || node.id === targetId) return false
    const size = nodeSize(node)
    const left = node.x - size.width - 12
    const right = node.x + size.width + 12
    const top = node.y - size.height - 12
    const bottom = node.y + size.height + (node.type.endsWith('Gateway') ? 29 : 12)
    return points.slice(1).some((point, index) => {
      const previous = points[index]!
      return previous.y === point.y
        ? previous.y >= top && previous.y <= bottom && Math.max(previous.x, point.x) > left && Math.min(previous.x, point.x) < right
        : previous.x >= left && previous.x <= right && Math.max(previous.y, point.y) > top && Math.min(previous.y, point.y) < bottom
    })
  })
}

/**
 * 将正交折线转为 SVG 路径。
 * @param points 折线端点。
 * @returns SVG path d。
 */
function toPath(points: ReadonlyArray<CanvasPoint>): string {
  return points.slice(1).reduce((path, point, index) => {
    const previous = points[index]!
    if (point.x !== previous.x) path += ` H ${point.x}`
    if (point.y !== previous.y) path += ` V ${point.y}`
    return path
  }, `M ${points[0]!.x} ${points[0]!.y}`)
}

/**
 * 把条件标签放在折线附近且不盖住节点。
 * @param points 折线端点。
 * @param nodes 画布节点。
 * @returns 标签中心。
 */
function labelPosition(points: ReadonlyArray<CanvasPoint>, nodes: ReadonlyArray<FlowNode>): CanvasPoint | null {
  const candidates: Array<{ point: CanvasPoint; length: number }> = []
  points.slice(1).forEach((point, index) => {
    const previous = points[index]!
    if (previous.y === point.y) {
      const centerX = (previous.x + point.x) / 2
      const length = Math.abs(point.x - previous.x)
      candidates.push({ point: { x: centerX, y: point.y - 20 }, length }, { point: { x: centerX, y: point.y + 24 }, length })
    } else {
      const centerY = (previous.y + point.y) / 2
      const length = Math.abs(point.y - previous.y)
      candidates.push({ point: { x: point.x + 64, y: centerY }, length: length / 2 }, { point: { x: point.x - 64, y: centerY }, length: length / 2 })
    }
  })
  // 标签有白底，周围额外留出间距，避免条件盖住节点文字。
  return candidates.sort((a, b) => b.length - a.length).find(candidate => nodes.every(node => {
    const size = nodeSize(node)
    return candidate.point.x + 59 < node.x - size.width || candidate.point.x - 59 > node.x + size.width || candidate.point.y + 18 < node.y - size.height || candidate.point.y - 18 > node.y + size.height + (node.type.endsWith('Gateway') ? 21 : 0)
  }))?.point ?? null
}

/**
 * 比较候选绕线路径的长度，优先使用较短的安全走线。
 * @param points 折线端点。
 * @returns 正交路径总长度。
 */
function pathLength(points: ReadonlyArray<CanvasPoint>): number {
  return points.slice(1).reduce((total, point, index) => total + Math.abs(point.x - points[index]!.x) + Math.abs(point.y - points[index]!.y), 0)
}

/**
 * 为向前、返工和跨分支连线选择可读的正交折线路径。
 * @param edge 连线。
 * @param source 起点节点。
 * @param target 终点节点。
 * @param nodes 全部画布节点。
 * @param edges 全部画布连线。
 * @returns 路径与条件标签位置。
 */
export function edgeGeometry(edge: FlowEdge, source: FlowNode, target: FlowNode, nodes: ReadonlyArray<FlowNode> = [source, target], edges: ReadonlyArray<FlowEdge> = [edge]): EdgeGeometry {
  const from = sidePort(source, portOffset(edge, source, edges, nodes, true), true)
  const to = sidePort(target, portOffset(edge, target, edges, nodes, false), false)
  const forward = to.x - from.x >= 52
  const middle = Math.round((from.x + to.x) / 2)
  const midpoints = [middle, from.x + 18, to.x - 18, Math.round((from.x * 3 + to.x) / 4), Math.round((from.x + to.x * 3) / 4)]
  const direct = forward ? midpoints.map(x => [from, { x, y: from.y }, { x, y: to.y }, to]) : []
  let points: CanvasPoint[] = direct.find(candidate => !intersectsNode(candidate, nodes, source.id, target.id) && (!(edge.condition || edge.isDefault) || labelPosition(candidate, nodes))) ?? []
  let label = points.length ? labelPosition(points, nodes) : null
  if (!points.length) {
    const minX = Math.min(from.x, to.x)
    const maxX = Math.max(from.x, to.x)
    const relevant = nodes.filter(node => node.x + nodeSize(node).width >= minX - 35 && node.x - nodeSize(node).width <= maxX + 35)
    const top = Math.min(...relevant.map(node => node.y - nodeSize(node).height)) - 48 - Math.max(0, edge.priority) * 12
    const bottom = Math.max(...relevant.map(node => node.y + nodeSize(node).height + (node.type.endsWith('Gateway') ? 22 : 0))) + 48 + Math.max(0, edge.priority) * 12
    const detour = (lane: number, gap: number): CanvasPoint[] => [from, { x: from.x + gap, y: from.y }, { x: from.x + gap, y: lane }, { x: to.x - gap, y: lane }, { x: to.x - gap, y: to.y }, to]
    const choices = [top, bottom].flatMap(lane => [26, 38, 50, 66].map(gap => detour(lane, gap)))
    points = choices.filter(candidate => !intersectsNode(candidate, nodes, source.id, target.id))
      .sort((a, b) => pathLength(a) - pathLength(b))[0] ?? choices[0]!
    label = labelPosition(points, nodes)
  }
  const fallback = points[1] ?? from
  return { path: toPath(points), points, labelX: Math.round(label?.x ?? fallback.x), labelY: Math.round(label?.y ?? fallback.y - 18) }
}

/**
 * 把视口像素位置映射到画布模型坐标。
 * @param clientX 指针横坐标。
 * @param clientY 指针纵坐标。
 * @param left SVG 左边界。
 * @param top SVG 上边界。
 * @param offsetX 画布平移横量。
 * @param offsetY 画布平移纵量。
 * @param zoom 当前缩放。
 * @returns 画布坐标。
 */
export function screenToCanvas(clientX: number, clientY: number, left: number, top: number, offsetX: number, offsetY: number, zoom: number): { x: number; y: number } {
  return { x: (clientX - left - offsetX) / zoom, y: (clientY - top - offsetY) / zoom }
}
