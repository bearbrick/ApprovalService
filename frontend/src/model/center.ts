/**
 * 审批中心的列表分组。
 */
export type CenterSection = 'todo' | 'done' | 'cc' | 'started'

/**
 * 审批中心可展示的操作；是否可执行由服务端逐单声明。
 */
export type CenterAction = 'approve' | 'handle' | 'acknowledge' | 'reject' | 'return' | 'transfer' | 'addSign' | 'removeSign' | 'consult' | 'reply' | 'supplement' | 'copy' | 'undoApproval' | 'comment' | 'saveDraft' | 'remind'

/**
 * 列表中的业务摘要字段，值已经由适配层转为只读文本。
 */
export interface CenterSummaryField {
  /**
   * 字段名。
   */
  key: string
  /**
   * 展示名称。
   */
  label: string
  /**
   * 展示值。
   */
  value: string
}

/**
 * 单条审批摘要，只包含当前用户有权查看的内容。
 */
export interface CenterItem {
  /**
   * 审批单标识。
   */
  id: string
  /**
   * 所在列表分组。
   */
  section: CenterSection
  /**
   * 审批标题。
   */
  title: string
  /**
   * 审批编号。
   */
  serialNo: string
  /**
   * 业务系统单号。
   */
  businessKey: string
  /**
   * 流程编码。
   */
  flowCode: string
  /**
   * 流程名称。
   */
  flowName: string
  /**
   * 来源业务系统。
   */
  sourceName: string
  /**
   * 发起人。
   */
  initiatorName: string
  /**
   * 当前节点或最新动作。
   */
  currentStep: string
  /**
   * 状态文字。
   */
  status: string
  /**
   * 带时区的 ISO 8601 发起时间。
   */
  createdAt: string
  /**
   * 带时区的 ISO 8601 最近更新时间。
   */
  updatedAt: string
  /**
   * 是否加急。
   */
  urgent: boolean
  /**
   * 当前用户是否未读。
   */
  unread: boolean
  /**
   * 服务端是否允许这项待办批量同意。
   */
  allowsBatchApprove: boolean
  /**
   * 当前人员经服务端确认可批量同意的任务 ID。
   */
  bulkTaskIds?: readonly string[]
  /**
   * 服务端当前准许的操作。
   */
  allowedActions: readonly CenterAction[]
  /**
   * 流程配置的摘要字段。
   */
  summary: readonly CenterSummaryField[]
}

/**
 * 审批中心列表筛选条件，空字符串表示不限制。
 */
export interface CenterFilters {
  /**
   * 标题、编号、业务单号或摘要全文。
   */
  search: string
  /**
   * 流程编码。
   */
  flowCode: string
  /**
   * 状态文字。
   */
  status: string
  /**
   * 起始日期，格式 YYYY-MM-DD。
   */
  dateFrom: string
  /**
   * 结束日期，格式 YYYY-MM-DD。
   */
  dateTo: string
}

/**
 * 审批详情的一条只读字段。
 */
export interface CenterDetailField {
  /**
   * 字段路径。
   */
  key: string
  /**
   * 展示名称。
   */
  label: string
  /**
   * 服务端许可展示的文本。
   */
  value: string
  /**
   * 明细表中按发布字段定义排序且已授权的列。
   */
  columns?: readonly CenterDetailColumn[]
  /**
   * 明细表中按列顺序展示的只读行。
   */
  rows?: readonly (readonly string[])[]
}

/**
 * 一列已授权明细字段的展示名称。
 */
export interface CenterDetailColumn {
  /**
   * 子字段编码。
   */
  key: string
  /**
   * 发布版本中的子字段名称。
   */
  label: string
}

/**
 * 当前人员可见的锁定版本详情排版。
 */
export interface CenterDetailLayout {
  /**
   * 自动排版、自定义排版或业务原单嵌入。
   */
  mode: 'auto' | 'custom' | 'embed'
  /**
   * 发布版本上的业务页面编码，仅嵌入方式具有。
   */
  embedPageCode?: string
  /**
   * 仅含当前人员可见字段的排版分组。
   */
  groups: readonly CenterDetailLayoutGroup[]
}

/**
 * 详情排版中的一个可见分组。
 */
export interface CenterDetailLayoutGroup {
  /**
   * 安全展示标题。
   */
  title: string
  /**
   * 每行列数。
   */
  columns: number
  /**
   * 按发布顺序排列的可见字段。
   */
  items: readonly CenterDetailLayoutItem[]
}

/**
 * 已授权详情字段的排版参数。
 */
export interface CenterDetailLayoutItem {
  /**
   * 已授权的只读展示字段。
   */
  field: CenterDetailField
  /**
   * 占用的分组列数。
   */
  columnSpan: number
  /**
   * 仅含可见明细列的宽度百分比。
   */
  detailColumnWidths: Readonly<Record<string, number>>
  /**
   * 已授权金额字段的人民币大写。
   */
  moneyUppercase?: string
}

/**
 * 当前审批详情中已授权且已关联的附件元数据。
 */
export interface CenterAttachment {
  /**
   * 服务端文件 ID。
   */
  id: string
  /**
   * 上传时的显示名称。
   */
  fileName: string
  /**
   * 实际媒体类型。
   */
  contentType: string
  /**
   * 文件字节数。
   */
  size: number
}

/**
 * 审批进度中的一条节点记录。
 */
export interface CenterTimelineEntry {
  /**
   * 稳定标识。
   */
  id: string
  /**
   * 节点或操作名称。
   */
  title: string
  /**
   * 处理人名称。
   */
  actor: string
  /**
   * 带时区的 ISO 8601 时间；未发生时为空。
   */
  occurredAt: string
  /**
   * 审批意见，只读。
   */
  opinion: string
  /**
   * 本次审计冻结的手写签名资产 ID。
   */
  signatureFileId?: string | null
  /**
   * 是否为当前节点。
   */
  current: boolean
}

/**
 * 一项登录人员当前有权办理的任务。
 */
export interface CenterActionTask {
  /**
   * 任务 ID。
   */
  taskId: string
  /**
   * 发布节点名称。
   */
  nodeName: string
  /**
   * 服务端当前许可的操作。
   */
  actions: readonly CenterAction[]
  /**
   * 成功办理时服务端发布规则是否要求本次手写签名。
   */
  signatureRequired?: boolean
  /**
   * 仅由服务端为当前人员和任务声明的关联目标。
   */
  relatedTargets?: readonly CenterRelatedTarget[]
}

/**
 * 征询、减签或退回的当前可选目标。
 */
export interface CenterRelatedTarget {
  /**
   * 适用的引擎操作编码。
   */
  action: string
  /**
   * 经服务端核验的目标标识。
   */
  id: string
  /**
   * 目标的安全展示说明。
   */
  label: string
}

/**
 * 只含已到达节点和当前节点的安全 BPMN 进度图。
 */
export interface CenterProgressGraph {
  /**
   * 已到达节点及当前高亮状态。
   */
  nodes: readonly { id: string, type: string, name: string, x: number, y: number, isActive: boolean }[]
  /**
   * 两端均已到达的连线。
   */
  edges: readonly { id: string, sourceId: string, targetId: string }[]
}

/**
 * 审批详情，业务数据始终作为只读快照呈现。
 */
export interface CenterDetail {
  /**
   * 当前发起人是否可以尝试催办。
   */
  canRemind?: boolean
  /**
   * 已授权安全进度图。
   */
  progress?: CenterProgressGraph
  /**
   * 本次详情实际包含的最大活动序号，仅用于确认已读，零表示没有活动。
   */
  readSequence: number
  /**
   * 与列表相同的摘要。
   */
  item: CenterItem
  /**
   * 每项当前可办理任务及该任务的服务端许可。
   */
  actionTasks?: readonly CenterActionTask[]
  /**
   * 当前人员原指派且尚未处理的任务，仍需在转委托写入口重验。
   */
  delegationTasks?: readonly { taskId: string, nodeName: string }[]
  /**
   * 由服务端授权后返回的业务字段快照。
   */
  fields: readonly CenterDetailField[]
  /**
   * 按已授权字段裁剪的发布版本详情排版。
   */
  layout: CenterDetailLayout
  /**
   * 审批进度与已发生的操作。
   */
  timeline: readonly CenterTimelineEntry[]
  /**
   * 可展示的附件名称；正式下载由独立权限接口处理。
   */
  attachmentNames: readonly string[]
  /**
   * 可经现有验权下载接口读取的附件。
   */
  attachments: readonly CenterAttachment[]
}

/**
 * 把不可信的日期文本转为排序数值，无效日期排在最后。
 * @param value 带时区的日期时间。
 * @returns 可比较的时间戳或最小值。
 */
function dateValue(value: string): number {
  const parsed = Date.parse(value)
  return Number.isFinite(parsed) ? parsed : Number.NEGATIVE_INFINITY
}

/**
 * 按当前列表、搜索词和日期筛选，并让待办中的加急项优先。
 * @param items 服务端已按可见权限过滤的摘要。
 * @param section 当前列表分组。
 * @param filters 用户填写的筛选条件。
 * @returns 新数组，原数据不被修改。
 */
export function filterCenterItems(items: readonly CenterItem[], section: CenterSection, filters: CenterFilters): CenterItem[] {
  const search = filters.search.trim().toLocaleLowerCase()
  return items.filter(item => {
    if (item.section !== section || (filters.flowCode && item.flowCode !== filters.flowCode) || (filters.status && item.status !== filters.status)) return false
    // 日期筛选按响应携带的日期部分比较，避免浏览器时区改变业务日期。
    const day = item.createdAt.slice(0, 10)
    if ((filters.dateFrom && day < filters.dateFrom) || (filters.dateTo && day > filters.dateTo)) return false
    if (!search) return true
    const searchable = [item.title, item.serialNo, item.businessKey, ...item.summary.map(field => field.value)].join(' ').toLocaleLowerCase()
    return searchable.includes(search)
  }).sort((a, b) => {
    if (section === 'todo' && a.urgent !== b.urgent) return a.urgent ? -1 : 1
    return dateValue(b.updatedAt) - dateValue(a.updatedAt)
  })
}

/**
 * 判断一条待办能否进入批量同意集合。
 * @param item 待检查的审批摘要。
 * @returns 服务端明确允许且属于待办时为真。
 */
export function canSelectBatchApprove(item: CenterItem): boolean {
  return item.section === 'todo' && item.allowsBatchApprove && item.allowedActions.includes('approve')
}

/**
 * 从当前可见的待办中提取有效选择，隐藏或失去权限的项目不会提交。
 * @param items 当前过滤后的可见摘要。
 * @param selectedIds 界面已选择的审批单标识。
 * @returns 仍可批量同意的标识。
 */
export function eligibleBatchIds(items: readonly CenterItem[], selectedIds: readonly string[]): string[] {
  const selected = new Set(selectedIds)
  return items.filter(item => selected.has(item.id) && canSelectBatchApprove(item)).map(item => item.id)
}

/**
 * 详情页只展示服务端准许且属于当前分组的操作。
 * @param item 审批摘要。
 * @returns 可展示的操作名称。
 */
export function visibleCenterActions(item: CenterItem): CenterAction[] {
  if (item.section === 'cc') return item.allowedActions.filter(action => action === 'comment')
  if (item.section === 'started') return item.allowedActions.filter(action => action === 'remind' || action === 'comment')
  return [...item.allowedActions]
}
