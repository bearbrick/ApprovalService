/**
 * 后端流程定义 JSON 的前端镜像；权威定义在 Contracts/Flows。
 */
/**
 * 当前自由画布格式版本。
 */
export const flowSchemaVersion = 1 as const;
/**
 * 自由画布节点种类，与后端 FlowNodeTypes 一一对应。
 */
export const flowNodeTypes = {
  Start: 'start',
  End: 'end',
  Approval: 'approval',
  Handling: 'handling',
  Cc: 'cc',
  ExclusiveGateway: 'exclusiveGateway',
  InclusiveGateway: 'inclusiveGateway',
  ParallelGateway: 'parallelGateway',
  Subprocess: 'subprocess',
  Automatic: 'automatic',
} as const;
/**
 * 审批数据字段种类，与后端 FlowFieldTypes 一一对应。
 */
export const flowFieldTypes = {
  Text: 'text',
  Number: 'number',
  Money: 'money',
  Date: 'date',
  Person: 'person',
  Department: 'department',
  Attachment: 'attachment',
  DetailTable: 'detailTable',
} as const;
/**
 * 网关在成对的分叉与汇合中的方向。
 */
export const flowGatewayDirections = {
  Split: 'split',
  Join: 'join',
} as const;
/**
 * 节点对字段的访问级别。
 */
export const flowFieldAccess = {
  Hidden: 'hidden',
  ReadOnly: 'readOnly',
  Editable: 'editable',
} as const;
/**
 * 画布节点种类的联合类型。
 */
export type FlowNodeType = (typeof flowNodeTypes)[keyof typeof flowNodeTypes];
/**
 * 审批数据字段种类的联合类型。
 */
export type FlowFieldType = (typeof flowFieldTypes)[keyof typeof flowFieldTypes];
/**
 * 网关方向的联合类型。
 */
export type FlowGatewayDirection = (typeof flowGatewayDirections)[keyof typeof flowGatewayDirections];
/**
 * 字段访问级别的联合类型。
 */
export type FlowFieldAccessType = (typeof flowFieldAccess)[keyof typeof flowFieldAccess];
/**
 * 一个流程版本的完整自由画布定义。
 */
export interface FlowDefinition {
  /**
   * 流程定义格式版本。
   */
  schemaVersion: number;
  /**
   * 业务系统使用的流程编码。
   */
  code: string;
  /**
   * 流程显示名称。
   */
  name: string;
  /**
   * 唯一的设计方式：自由画布。
   */
  canvas: FlowCanvas;
  /**
   * 审批数据字段和摘要。
   */
  data: FlowDataDefinition;
  /**
   * 只读详情的展示排版。
   */
  detailLayout: FlowDetailLayout;
  /**
   * 审批单打印设置。
   */
  printSettings: FlowPrintSettings;
  /**
   * 可发起的接入应用编码；空集合表示由授权策略决定。
   */
  allowedClientApps: string[];
  /**
   * 可发起的部门编码；空集合表示不额外限制。
   */
  allowedInitiatorDepartments: string[];
  /**
   * 可发起的角色编码；空集合表示不额外限制。
   */
  allowedInitiatorRoles: string[];
}
/**
 * 节点、连线及设计坐标。
 */
export interface FlowCanvas {
  /**
   * 画布节点。
   */
  nodes: FlowNode[];
  /**
   * 画布连线。
   */
  edges: FlowEdge[];
}
/**
 * 自由画布节点。
 */
export interface FlowNode {
  /**
   * 流程内稳定且唯一的节点 ID。
   */
  id: string;
  /**
   * 节点种类。
   */
  type: FlowNodeType;
  /**
   * 节点显示名称。
   */
  name: string;
  /**
   * 画布横坐标，不参与运行语义。
   */
  x: number;
  /**
   * 画布纵坐标，不参与运行语义。
   */
  y: number;
  /**
   * 一对分叉与汇合共享的 ID。
   */
  gatewayPairId?: string | null;
  /**
   * 网关方向。
   */
  gatewayDirection?: FlowGatewayDirection | null;
  /**
   * 人工节点的人员来源。
   */
  assignee?: FlowAssignee | null;
  /**
   * 人工节点的审批规则。
   */
  approval?: FlowApprovalSettings | null;
  /**
   * 子流程设置。
   */
  subprocess?: FlowSubprocessSettings | null;
  /**
   * 自动节点设置。
   */
  automation?: FlowAutomationSettings | null;
  /**
   * 当前节点对审批字段的权限。
   */
  fieldPermissions: FlowFieldPermission[];
}
/**
 * 自由画布有向连线。
 */
export interface FlowEdge {
  /**
   * 流程内稳定且唯一的连线 ID。
   */
  id: string;
  /**
   * 源节点 ID。
   */
  sourceId: string;
  /**
   * 目标节点 ID。
   */
  targetId: string;
  /**
   * 条件优先级，数字越小越先尝试。
   */
  priority: number;
  /**
   * 是否为无其他条件满足时的兜底线。
   */
  isDefault: boolean;
  /**
   * 有限表达式，字段引用写成 ${fieldKey}。
   */
  condition?: string | null;
}
/**
 * 从组织架构或业务系统寻找节点处理人的配置。
 */
export interface FlowAssignee {
  /**
   * 人员来源编码。
   */
  source: string;
  /**
   * 成员、角色、岗位、矩阵或接口编码。
   */
  value?: string | null;
  /**
   * 引用的审批字段 key。
   */
  fieldKey?: string | null;
  /**
   * 上级或上级部门级数。
   */
  level?: number | null;
}
/**
 * 人工节点的多人审批和异常处理。
 */
export interface FlowApprovalSettings {
  /**
   * 多人方式：sequential、all、any、vote。
   */
  mode: string;
  /**
   * 票签通过比例，0 至 1。
   */
  voteThreshold?: number | null;
  /**
   * 按人员 ID 设置的票签权重。
   */
  voteWeights: Record<string, number>;
  /**
   * 会签拒绝处理：immediate 或 waitAll。
   */
  rejectBehavior: string;
  /**
   * 无处理人时：block、approve、administrator 或 specified。
   */
  missingAssigneeBehavior: string;
  /**
   * 处理人是发起人时：skip、self 或 manager。
   */
  selfApprovalBehavior: string;
  /**
   * 重复出现同一人时：adjacentOnce、once 或 everyTime。
   */
  duplicateAssigneeBehavior: string;
  /**
   * 处理人离职时：manager 或 administrator。
   */
  inactiveAssigneeBehavior: string;
  /**
   * 当前节点允许的操作编码。
   */
  allowedActions: string[];
  /**
   * 审批意见是否必填。
   */
  commentRequired: boolean;
  /**
   * 抄送节点是否需要确认已阅。
   */
  readReceiptRequired: boolean;
  /**
   * 提醒时限，单位分钟；空值表示不启用。
   */
  dueMinutes?: number | null;
}
/**
 * 子流程调用设置。
 */
export interface FlowSubprocessSettings {
  /**
   * 被调用流程的编码。
   */
  flowCode: string;
  /**
   * 是否等待子流程完成。
   */
  waitForCompletion: boolean;
  /**
   * 子流程字段 key 到当前流程字段 key 的映射。
   */
  dataMappings: Record<string, string>;
}
/**
 * 自动节点向业务系统发出的动作设置。
 */
export interface FlowAutomationSettings {
  /**
   * 接入应用配置的动作编码。
   */
  actionCode: string;
  /**
   * 耗尽重试后是否交管理员处理。
   */
  escalateToAdministrator: boolean;
}
/**
 * 当前节点对字段的展示与填写权限。
 */
export interface FlowFieldPermission {
  /**
   * 字段 key，明细子字段写成 tableKey.childKey。
   */
  fieldKey: string;
  /**
   * 隐藏、只读或可填写。
   */
  access: FlowFieldAccessType;
}
/**
 * 审批数据字段及待办摘要。
 */
export interface FlowDataDefinition {
  /**
   * 业务系统提交及审批人填写的字段。
   */
  fields: FlowFieldDefinition[];
  /**
   * 摘要显示的字段 key，通常选三至五个。
   */
  summaryFields: string[];
}
/**
 * 一个审批数据字段。
 */
export interface FlowFieldDefinition {
  /**
   * 同一层级内唯一的字段 key。
   */
  key: string;
  /**
   * 展示名称。
   */
  name: string;
  /**
   * 字段种类。
   */
  type: FlowFieldType;
  /**
   * 提交或填写时是否必传。
   */
  required: boolean;
  /**
   * 计算表达式，仅用于审批，不写回业务单据。
   */
  formula?: string | null;
  /**
   * 是否由审批人在获授权的节点填写。
   */
  approverInput: boolean;
  /**
   * 明细表的子字段。
   */
  children: FlowFieldDefinition[];
}
/**
 * 审批详情的只读展示方式。
 */
export interface FlowDetailLayout {
  /**
   * auto、custom 或 embed。
   */
  mode: string;
  /**
   * 拖拽排版的分组。
   */
  groups: FlowLayoutGroup[];
  /**
   * 嵌入业务系统原单时的页面编码。
   */
  embedPageCode?: string | null;
}
/**
 * 拖拽排版中的字段分组。
 */
export interface FlowLayoutGroup {
  /**
   * 分组标题。
   */
  title: string;
  /**
   * 一行的字段列数。
   */
  columns: number;
  /**
   * 按展示顺序排列的字段和明细表。
   */
  items: FlowLayoutItem[];
}
/**
 * 一个字段或明细表的展示配置。
 */
export interface FlowLayoutItem {
  /**
   * 字段 key。
   */
  fieldKey: string;
  /**
   * 占用的布局列数。
   */
  columnSpan: number;
  /**
   * 明细子字段列宽百分比。
   */
  detailColumnWidths: Record<string, number>;
  /**
   * 金额旁是否显示人民币大写。
   */
  showMoneyUppercase: boolean;
}
/**
 * 审批单的打印设置。
 */
export interface FlowPrintSettings {
  /**
   * A4 纸张方向：portrait 或 landscape。
   */
  orientation: string;
  /**
   * 按顺序打印的审批字段 key。
   */
  fieldKeys: string[];
  /**
   * 是否打印审批意见。
   */
  showComments: boolean;
  /**
   * 是否打印审批人签名。
   */
  showSignatures: boolean;
  /**
   * 是否附票据图片页。
   */
  includeReceiptPages: boolean;
  /**
   * 是否显示单位 Logo。
   */
  showLogo: boolean;
  /**
   * 是否显示二维码。
   */
  showQrCode: boolean;
  /**
   * 是否显示打印人和打印时间水印。
   */
  showWatermark: boolean;
}
/**
 * 编译后的运行图，不包含画布位置。
 */
export interface FlowExecutionGraph {
  /**
   * 格式版本。
   */
  schemaVersion: number;
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
