import type { CenterProgressGraph, CenterRelatedTarget } from '../model/center.ts'

/**
 * 验证服务端的安全进度图，只接受节点和两端均可见的连线。
 * @param value 待解析响应片段。
 * @returns 可显示图形，畸形值回退为空图。
 */
export function readCenterProgress(value: unknown): CenterProgressGraph {
  const empty: CenterProgressGraph = { nodes: [], edges: [] }
  if (!value || typeof value !== 'object' || Array.isArray(value)) return empty
  const wire = value as Record<string, unknown>
  if (!Array.isArray(wire.nodes) || !Array.isArray(wire.edges) || wire.nodes.length > 500 || wire.edges.length > 1000) return empty
  const nodes = wire.nodes.filter(node => node && typeof node === 'object' &&
    ['id', 'type', 'name'].every(key => typeof node[key] === 'string') &&
    Number.isFinite(node.x) && Number.isFinite(node.y) && typeof node.isActive === 'boolean')
  if (nodes.length !== wire.nodes.length) return empty
  const ids = new Set(nodes.map(node => node.id as string))
  const edges = wire.edges.filter(edge => edge && typeof edge === 'object' &&
    ['id', 'sourceId', 'targetId'].every(key => typeof edge[key] === 'string') &&
    ids.has(edge.sourceId) && ids.has(edge.targetId))
  if (edges.length !== wire.edges.length || ids.size !== nodes.length) return empty
  return { nodes, edges } as CenterProgressGraph
}

/**
 * 校验关联目标仅包含可操作的 ID 与安全标签。
 * @param value 待解析任务目标。
 * @returns 可选目标列表；畸形响应返回空列表。
 */
export function readCenterRelatedTargets(value: unknown): CenterRelatedTarget[] {
  if (!Array.isArray(value) || value.length > 100) return []
  if (!value.every(target => target && typeof target === 'object' &&
    ['action', 'id', 'label'].every(key => typeof target[key] === 'string' && target[key].length <= 200))) return []
  return value as CenterRelatedTarget[]
}
