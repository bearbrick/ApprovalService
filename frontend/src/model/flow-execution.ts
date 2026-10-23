import type { FlowEdge, FlowNode, FlowReturnMode } from './flow.ts';

/**
 * 编译后的运行图，不包含画布位置。
 */
export interface FlowExecutionGraph {
  /**
   * 格式版本。
   */
  schemaVersion: number;
  /**
   * 发布时固定的撤回限制；旧图省略时审批结束前均可撤回。
   */
  withdrawBeforeFirstDecision?: boolean;
  /**
   * 发布时固定的退回重提走法；旧图省略时从头开始。
   */
  returnMode?: FlowReturnMode;
  /**
   * 运行节点。
   */
  nodes: FlowExecutionNode[];
  /**
   * 运行连线。
   */
  edges: FlowExecutionEdge[];
}

/**
 * 运行节点，仅保留语义字段。
 */
export type FlowExecutionNode = Omit<FlowNode, 'x' | 'y'>;

/**
 * 运行连线，保留条件、优先级与兜底标记。
 */
export type FlowExecutionEdge = FlowEdge;
