import type { FlowApprovalSettings, FlowDefinition, FlowEdge, FlowNode } from '../../model/flow.ts'
import purchase from './samples/purchase-request.json' with { type: 'json' }
import expense from './samples/expense.json' with { type: 'json' }
import quality from './samples/quality-exception.json' with { type: 'json' }

/**
 * 可定位到节点、连线或字段的一条设计问题。
 */
export interface DesignerIssue {
  /**
   * 对应画布元素或字段 key；整体问题为空。
   */
  elementId: string | null
  /**
   * 给设计人员看的中文说明。
   */
  message: string
}

/**
 * 内置模板及其简短用途。
 */
export interface FlowTemplate {
  /**
   * 业务系统使用的模板编码。
   */
  code: string
  /**
   * 模板名称。
   */
  name: string
  /**
   * 模板覆盖的审批场景。
   */
  description: string
  /**
   * 模板中的流程定义。
   */
  definition: FlowDefinition
}

/**
 * 人工节点规则的安全默认值；与后端契约的默认值保持一致。
 */
export const defaultApproval: FlowApprovalSettings = {
  mode: 'sequential', voteWeights: {}, rejectBehavior: 'immediate', missingAssigneeBehavior: 'block',
  selfApprovalBehavior: 'skip', duplicateAssigneeBehavior: 'adjacentOnce', inactiveAssigneeBehavior: 'manager',
  allowedActions: ['approve', 'reject'], commentRequired: false, readReceiptRequired: false,
}

/**
 * 创建与正式契约一致的空白流程，给新流程和恢复失败时使用。
 * @param code 流程编码。
 * @returns 可立即编辑的独立草稿。
 */
export function createEmptyFlow(code = 'NEW_FLOW'): FlowDefinition {
  return {
    schemaVersion: 1, code, name: '新流程',
    canvas: { nodes: [
      { id: 'start', type: 'start', name: '开始', x: 80, y: 220, fieldPermissions: [] },
      { id: 'end', type: 'end', name: '结束', x: 480, y: 220, fieldPermissions: [] },
    ], edges: [{ id: 'start-end', sourceId: 'start', targetId: 'end', priority: 0, isDefault: false }] },
    data: { fields: [], summaryFields: [] }, detailLayout: { mode: 'auto', groups: [] },
    printSettings: { orientation: 'portrait', fieldKeys: [], showComments: true, showSignatures: true,
      includeReceiptPages: false, showLogo: true, showQrCode: true, showWatermark: false },
    allowedClientApps: [], allowedInitiatorDepartments: [], allowedInitiatorRoles: [],
  }
}

/**
 * 深拷贝流程；编辑器总是向父级返回新对象。
 * @param definition 原草稿。
 * @returns 独立的流程对象。
 */
export function copyFlow(definition: FlowDefinition): FlowDefinition {
  return JSON.parse(JSON.stringify(definition)) as FlowDefinition
}

/**
 * 把随附的后端示例补齐前端需要的可选字段，不改变示例的审批语义。
 * @param source 仓库中随附的流程示例。
 * @returns 独立且字段齐全的草稿。
 */
export function normalizeSample(source: FlowDefinition): FlowDefinition {
  const next = copyFlow(source)
  next.allowedClientApps ??= []
  next.allowedInitiatorDepartments ??= []
  next.allowedInitiatorRoles ??= []
  next.data ??= { fields: [], summaryFields: [] }
  next.data.summaryFields ??= []
  next.detailLayout ??= { mode: 'auto', groups: [] }
  next.detailLayout.groups ??= []
  next.printSettings = { ...createEmptyFlow().printSettings, ...next.printSettings }
  for (const node of next.canvas.nodes) {
    node.fieldPermissions ??= []
    if (node.approval) node.approval = { ...defaultApproval, ...node.approval, voteWeights: node.approval.voteWeights ?? {}, allowedActions: node.approval.allowedActions ?? [] }
  }
  return next
}

/**
 * 从本仓库后端正式示例复制的常用流程模板。
 */
export const flowTemplates: readonly FlowTemplate[] = [
  { code: 'PURCHASE_REQUEST', name: '采购申请', description: '部门审批、金额分支与采购执行', definition: normalizeSample(purchase as unknown as FlowDefinition) },
  { code: 'EXPENSE', name: '费用报销', description: '金额条件、会签与财务办理', definition: normalizeSample(expense as unknown as FlowDefinition) },
  { code: 'QUALITY_EXCEPTION', name: '质量异常', description: '返工回路与并行会审', definition: normalizeSample(quality as unknown as FlowDefinition) },
]

/**
 * 找到当前草稿里选中的节点。
 * @param definition 流程草稿。
 * @param id 节点 ID。
 * @returns 对应节点，找不到时为空。
 */
export function findNode(definition: FlowDefinition, id: string): FlowNode | undefined {
  return definition.canvas.nodes.find(node => node.id === id)
}

/**
 * 找到当前草稿里选中的连线。
 * @param definition 流程草稿。
 * @param id 连线 ID。
 * @returns 对应连线，找不到时为空。
 */
export function findEdge(definition: FlowDefinition, id: string): FlowEdge | undefined {
  return definition.canvas.edges.find(edge => edge.id === id)
}

/**
 * 替换一个节点，保留其余流程字段。
 * @param definition 当前草稿。
 * @param replacement 新节点。
 * @returns 新流程草稿。
 */
export function replaceNode(definition: FlowDefinition, replacement: FlowNode): FlowDefinition {
  const next = copyFlow(definition)
  next.canvas.nodes = next.canvas.nodes.map(node => node.id === replacement.id ? replacement : node)
  return next
}

/**
 * 替换一条连线，保留其余流程字段。
 * @param definition 当前草稿。
 * @param replacement 新连线。
 * @returns 新流程草稿。
 */
export function replaceEdge(definition: FlowDefinition, replacement: FlowEdge): FlowDefinition {
  const next = copyFlow(definition)
  next.canvas.edges = next.canvas.edges.map(edge => edge.id === replacement.id ? replacement : edge)
  return next
}
