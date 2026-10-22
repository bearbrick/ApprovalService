/**
 * SVG 连线几何与视图变换，不依赖浏览器，供画布交互和检查程序共用。
 */
import type { FlowEdge, FlowNode } from '../../model/flow.ts'

/**
 * 画布节点的交互半宽。
 */
export const nodeHalfWidth = 76
/**
 * 画布节点的交互半高。
 */
export const nodeHalfHeight = 29

/**
 * 一条线的 SVG 绘制结果。
 */
export interface EdgeGeometry {
  /**
   * 主线条的 SVG path d。
   */
  path: string
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
 * 为向前、返工和跨分支连线选择可读的折线路径。
 * @param edge 连线。
 * @param source 起点节点。
 * @param target 终点节点。
 * @returns 路径与条件标签位置。
 */
export function edgeGeometry(edge: FlowEdge, source: FlowNode, target: FlowNode): EdgeGeometry {
  const sx = source.x + nodeHalfWidth
  const sy = source.y
  const tx = target.x - nodeHalfWidth
  const ty = target.y
  if (tx > sx + 44) {
    const mid = Math.round((sx + tx) / 2)
    return { path: `M ${sx} ${sy} H ${mid} V ${ty} H ${tx}`, labelX: mid, labelY: Math.min(sy, ty) - 12 }
  }
  // 回路从上方或下方绕过节点，避免盖住中间的卡片。
  const lane = Math.min(sy, ty) - 78 - (edge.priority % 3) * 24
  return { path: `M ${sx} ${sy} H ${sx + 30} V ${lane} H ${tx - 30} V ${ty} H ${tx}`, labelX: (sx + tx) / 2, labelY: lane - 12 }
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
