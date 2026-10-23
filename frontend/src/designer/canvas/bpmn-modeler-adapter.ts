/**
 * 标准 BPMN 图元与审批服务流程定义之间的最小转换。
 */
import type { FlowCanvas, FlowNodeType } from '../../model/flow.ts'
import { createCanvasNode } from './canvas-model.ts'

/**
 * 建模器暴露给适配层的图元数据。
 */
export interface BpmnDiagramElement {
  /**
   * 图元标识。
   */
  id: string
  /**
   * BPMN 元素类型。
   */
  type: string
  /**
   * 图元左上角横坐标。
   */
  x?: number
  /**
   * 图元左上角纵坐标。
   */
  y?: number
  /**
   * 图元宽度。
   */
  width?: number
  /**
   * 图元高度。
   */
  height?: number
  /**
   * 连线来源。
   */
  source?: BpmnDiagramElement
  /**
   * 连线目标。
   */
  target?: BpmnDiagramElement
  /**
   * 图元的 BPMN 属性。
   */
  businessObject?: { name?: string }
  /**
   * 外置文字所附着的元素。
   */
  labelTarget?: BpmnDiagramElement
}

/**
 * 取得审批节点对应的标准 BPMN 形状。
 * @param type 审批服务节点类型。
 * @returns BPMN 类型。
 */
export function bpmnTypeFor(type: FlowNodeType): string {
  const types: Record<FlowNodeType, string> = {
    start: 'bpmn:StartEvent', end: 'bpmn:EndEvent', approval: 'bpmn:UserTask',
    handling: 'bpmn:UserTask', cc: 'bpmn:SendTask',
    exclusiveGateway: 'bpmn:ExclusiveGateway', inclusiveGateway: 'bpmn:InclusiveGateway',
    parallelGateway: 'bpmn:ParallelGateway', subprocess: 'bpmn:CallActivity',
    automatic: 'bpmn:ServiceTask',
  }
  return types[type]
}

/**
 * 还原新建标准图元的审批类型；已有节点的细分类型沿用原配置。
 * @param type BPMN 类型。
 * @returns 支持的审批节点类型，或空值。
 */
export function flowTypeFor(type: string): FlowNodeType | null {
  const types: Record<string, FlowNodeType> = {
    'bpmn:StartEvent': 'start', 'bpmn:EndEvent': 'end', 'bpmn:UserTask': 'approval',
    'bpmn:SendTask': 'cc', 'bpmn:ExclusiveGateway': 'exclusiveGateway',
    'bpmn:InclusiveGateway': 'inclusiveGateway', 'bpmn:ParallelGateway': 'parallelGateway',
    'bpmn:CallActivity': 'subprocess', 'bpmn:ServiceTask': 'automatic',
  }
  return types[type] ?? null
}

/**
 * 从建模器读取节点与连线，完整保留已有审批配置。
 * @param elements 图形注册表中的元素。
 * @param previous 上一步流程图配置。
 * @param requestedTypes 工具箱中刚创建节点的细分类型。
 * @returns 更新后的审批服务流程图。
 */
export function canvasFromBpmn(elements: BpmnDiagramElement[], previous: FlowCanvas, requestedTypes: ReadonlyMap<string, FlowNodeType>): FlowCanvas {
  const previousNodes = new Map(previous.nodes.map(node => [node.id, node]))
  const previousEdges = new Map(previous.edges.map(edge => [edge.id, edge]))
  const nodes = elements.filter(element => !element.labelTarget && flowTypeFor(element.type)).map(element => {
    const old = previousNodes.get(element.id)
    const type = requestedTypes.get(element.id) ?? old?.type ?? flowTypeFor(element.type)!
    const x = Math.round((element.x ?? 0) + (element.width ?? 0) / 2)
    const y = Math.round((element.y ?? 0) + (element.height ?? 0) / 2)
    const base = old ?? createCanvasNode(type, element.id, x, y)
    return { ...base, type, x, y, name: element.businessObject?.name || base.name }
  })
  const nodeIds = new Set(nodes.map(node => node.id))
  const edges = elements.filter(element => element.type === 'bpmn:SequenceFlow' && element.source && element.target && nodeIds.has(element.source.id) && nodeIds.has(element.target.id)).map(element => {
    const old = previousEdges.get(element.id)
    return {
      id: element.id, sourceId: element.source!.id, targetId: element.target!.id,
      priority: old?.priority ?? previous.edges.filter(edge => edge.sourceId === element.source!.id).length,
      isDefault: old?.isDefault ?? false, condition: old?.condition,
    }
  })
  const nodeOrder = new Map(previous.nodes.map((node, index) => [node.id, index]))
  const edgeOrder = new Map(previous.edges.map((edge, index) => [edge.id, index]))
  nodes.sort((a, b) => (nodeOrder.get(a.id) ?? Number.MAX_SAFE_INTEGER) - (nodeOrder.get(b.id) ?? Number.MAX_SAFE_INTEGER))
  edges.sort((a, b) => (edgeOrder.get(a.id) ?? Number.MAX_SAFE_INTEGER) - (edgeOrder.get(b.id) ?? Number.MAX_SAFE_INTEGER))
  return { nodes, edges }
}
