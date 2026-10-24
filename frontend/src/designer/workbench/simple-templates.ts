import type { FlowDefinition, FlowFieldDefinition, FlowFieldType } from '../../model/flow.ts'

/**
 * 无业务单据的通用模板说明。
 */
export interface SimpleFlowTemplate {
  /**
   * 业务系统可替换的流程编码。
   */
  code: string
  /**
   * 模板名称。
   */
  name: string
  /**
   * 模板适用的常见审批场景。
   */
  description: string
  /**
   * 可复制的自由画布和审批数据定义。
   */
  definition: FlowDefinition
}

/**
 * 模板字段的简短声明；运行时仍使用正式流程字段模型。
 */
interface TemplateField {
  /**
   * 业务系统传入的字段键。
   */
  key: string
  /**
   * 审批中心显示名称。
   */
  name: string
  /**
   * 正式契约字段类型。
   */
  type: FlowFieldType
  /**
   * 发起时是否必填。
   */
  required: boolean
}

/**
 * 不同模板共享的流程骨架，只包含审批定义和业务数据字段。
 */
interface TemplatePlan {
  /**
   * 模板流程编码。
   */
  code: string
  /**
   * 模板名称。
   */
  name: string
  /**
   * 模板说明。
   */
  description: string
  /**
   * 第二环节的当前组织角色编码。
   */
  reviewerRole: string
  /**
   * 第二环节名称。
   */
  reviewerName: string
  /**
   * 模板审批数据字段。
   */
  fields: TemplateField[]
}

/**
 * 给两个人工节点提供相同的保守默认权限。
 * @returns 可独立修改的节点规则。
 */
function approvalSettings() {
  return {
    mode: 'sequential', voteWeights: {}, rejectBehavior: 'immediate', missingAssigneeBehavior: 'block',
    selfApprovalBehavior: 'skip', duplicateAssigneeBehavior: 'adjacentOnce', inactiveAssigneeBehavior: 'manager',
    allowedActions: ['approve', 'reject', 'return'], commentRequired: true, readReceiptRequired: false,
  }
}

/**
 * 把模板字段转换成正式的业务数据定义。
 * @param field 模板字段声明。
 * @returns 没有审批人写入权限的独立字段。
 */
function dataField(field: TemplateField): FlowFieldDefinition {
  return { ...field, approverInput: false, businessUpdateAllowed: false, children: [] }
}

/**
 * 创建可在设计器继续修改的两级审批图；模板不产生业务单据。
 * @param plan 模板配置。
 * @returns 完整且可发布预检的流程定义。
 */
function buildTemplate(plan: TemplatePlan): FlowDefinition {
  return {
    schemaVersion: 1, code: plan.code, name: plan.name,
    canvas: {
      nodes: [
        { id: 'start', type: 'start', name: '业务系统发起', x: 80, y: 220, fieldPermissions: [] },
        { id: 'manager', type: 'approval', name: '直属主管审批', x: 300, y: 220,
          assignee: { source: 'manager' }, approval: approvalSettings(), fieldPermissions: [] },
        { id: 'reviewer', type: 'approval', name: plan.reviewerName, x: 520, y: 220,
          assignee: { source: 'role', value: plan.reviewerRole }, approval: approvalSettings(), fieldPermissions: [] },
        { id: 'end', type: 'end', name: '结束', x: 740, y: 220, fieldPermissions: [] },
      ],
      edges: [
        { id: 'start-manager', sourceId: 'start', targetId: 'manager', priority: 0, isDefault: false },
        { id: 'manager-reviewer', sourceId: 'manager', targetId: 'reviewer', priority: 0, isDefault: false },
        { id: 'reviewer-end', sourceId: 'reviewer', targetId: 'end', priority: 0, isDefault: false },
      ],
    },
    data: { fields: plan.fields.map(dataField), summaryFields: plan.fields.slice(0, 3).map(field => field.key) },
    detailLayout: { mode: 'auto', groups: [] },
    printSettings: { orientation: 'portrait', fieldKeys: plan.fields.map(field => field.key), showComments: true,
      showSignatures: true, includeReceiptPages: false, showLogo: true, showQrCode: true, showWatermark: false },
    allowedClientApps: [], allowedInitiatorDepartments: [], allowedInitiatorRoles: [],
    withdrawBeforeFirstDecision: false, returnMode: 'restart', isConfidential: false,
  }
}

/**
 * 内置的七种标准流程骨架；模板只有审批和字段，不包含业务原单。
 */
const plans: readonly TemplatePlan[] = [
  { code: 'LEAVE_REQUEST', name: '请假审批', description: '请假日期、天数与人事复核', reviewerRole: 'HR_REVIEWER', reviewerName: '人事复核', fields: [
    { key: 'leaveType', name: '请假类型', type: 'text', required: true },
    { key: 'startDate', name: '开始日期', type: 'date', required: true },
    { key: 'endDate', name: '结束日期', type: 'date', required: true },
    { key: 'days', name: '请假天数', type: 'number', required: true },
    { key: 'reason', name: '请假原因', type: 'text', required: true },
  ] },
  { code: 'OVERTIME_REQUEST', name: '加班审批', description: '加班时长与人事复核', reviewerRole: 'HR_REVIEWER', reviewerName: '人事复核', fields: [
    { key: 'workDate', name: '加班日期', type: 'date', required: true },
    { key: 'hours', name: '预计小时', type: 'number', required: true },
    { key: 'reason', name: '加班事由', type: 'text', required: true },
  ] },
  { code: 'BUSINESS_TRIP', name: '出差审批', description: '行程与费用预算复核', reviewerRole: 'FINANCE_REVIEWER', reviewerName: '财务复核', fields: [
    { key: 'destination', name: '目的地', type: 'text', required: true },
    { key: 'departureDate', name: '出发日期', type: 'date', required: true },
    { key: 'returnDate', name: '返回日期', type: 'date', required: true },
    { key: 'budget', name: '费用预算', type: 'money', required: true },
  ] },
  { code: 'PAYMENT_REQUEST', name: '付款审批', description: '付款金额、收款方与财务复核', reviewerRole: 'FINANCE_REVIEWER', reviewerName: '财务复核', fields: [
    { key: 'payee', name: '收款方', type: 'text', required: true },
    { key: 'amount', name: '付款金额', type: 'money', required: true },
    { key: 'purpose', name: '付款用途', type: 'text', required: true },
    { key: 'evidence', name: '付款依据', type: 'attachment', required: false },
  ] },
  { code: 'SEAL_USE', name: '用印审批', description: '印章种类、文件与行政复核', reviewerRole: 'ADMIN_REVIEWER', reviewerName: '行政复核', fields: [
    { key: 'documentTitle', name: '文件名称', type: 'text', required: true },
    { key: 'sealType', name: '印章种类', type: 'text', required: true },
    { key: 'useDate', name: '用印日期', type: 'date', required: true },
    { key: 'document', name: '用印文件', type: 'attachment', required: true },
  ] },
  { code: 'CONTRACT_REVIEW', name: '合同审批', description: '合同相对方、金额与法务复核', reviewerRole: 'LEGAL_REVIEWER', reviewerName: '法务复核', fields: [
    { key: 'counterparty', name: '合同相对方', type: 'text', required: true },
    { key: 'contractAmount', name: '合同金额', type: 'money', required: true },
    { key: 'effectiveDate', name: '生效日期', type: 'date', required: false },
    { key: 'contractFile', name: '合同文件', type: 'attachment', required: true },
  ] },
  { code: 'ITEM_REQUISITION', name: '物品领用审批', description: '物品、数量与库管复核', reviewerRole: 'WAREHOUSE_REVIEWER', reviewerName: '库管复核', fields: [
    { key: 'itemName', name: '物品名称', type: 'text', required: true },
    { key: 'quantity', name: '领用数量', type: 'number', required: true },
    { key: 'reason', name: '领用原因', type: 'text', required: true },
  ] },
]

/**
 * 可直接复制到设计工作台的新增通用模板。
 */
export const simpleFlowTemplates: readonly SimpleFlowTemplate[] = plans.map(plan => ({
  code: plan.code, name: plan.name, description: plan.description, definition: buildTemplate(plan),
}))
