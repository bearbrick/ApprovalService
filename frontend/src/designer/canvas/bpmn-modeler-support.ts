/**
 * BPMN 建模器的类型边界、空流程文档和图元配色。
 */
import type { FlowNodeType } from '../../model/flow.ts'
import type { BpmnDiagramElement } from './bpmn-modeler-adapter.ts'

/**
 * 建模器按名称暴露的服务；只声明画布实际调用的方法。
 */
export interface ModelerServices {
  /**
   * 图形视口、根元素与问题标记。
   */
  canvas: { getRootElement(): BpmnDiagramElement; zoom(value?: number | string): number; viewbox(value?: { x: number; y: number; width: number; height: number }): { x: number; y: number; width: number; height: number }; addMarker(id: string, marker: string): void; removeMarker(id: string, marker: string): void; scrollToElement(element: BpmnDiagramElement): void }
  /**
   * BPMN 图形操作。
   */
  modeling: { createShape(shape: unknown, position: { x: number; y: number }, parent: BpmnDiagramElement): BpmnDiagramElement; connect(source: BpmnDiagramElement, target: BpmnDiagramElement, attributes?: object): BpmnDiagramElement | undefined; moveShape(shape: BpmnDiagramElement, delta: { x: number; y: number }): void; removeElements(elements: BpmnDiagramElement[]): void; setColor(elements: BpmnDiagramElement[], colors: { fill?: string; stroke?: string }): void }
  /**
   * 图元工厂。
   */
  elementFactory: { createShape(attributes: object): unknown }
  /**
   * BPMN 数据工厂。
   */
  bpmnFactory: { create(type: string, attributes: object): unknown }
  /**
   * 全部图元及按标识查询。
   */
  elementRegistry: { get(id: string): BpmnDiagramElement | undefined; getAll(): BpmnDiagramElement[] }
  /**
   * 当前图元选择。
   */
  selection: { select(element: BpmnDiagramElement | null): void }
}

/**
 * 建立只有一个流程容器的标准 BPMN 文档。
 * @returns 可由 bpmn-js 导入的空图。
 */
export function emptyDiagram(): string {
  return '<?xml version="1.0" encoding="UTF-8"?><bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" id="Definitions_1" targetNamespace="urn:approvalservice:editor"><bpmn:process id="Process_1" isExecutable="false"/><bpmndi:BPMNDiagram id="Diagram_1"><bpmndi:BPMNPlane id="Plane_1" bpmnElement="Process_1"/></bpmndi:BPMNDiagram></bpmn:definitions>'
}

/**
 * 为 BPMN 图形采用与标签服务一致的低饱和配色。
 * @param type 审批节点类型。
 * @returns 填充与描边颜色。
 */
export function nodeColors(type: FlowNodeType): { fill: string; stroke: string } {
  if (type === 'start' || type === 'end') return { fill: '#eaf7ef', stroke: '#58a978' }
  if (type.endsWith('Gateway')) return { fill: '#fff8ec', stroke: '#d8ac63' }
  return { fill: '#f7faff', stroke: '#6594d1' }
}
