/**
 * 管理后台的内容形式。
 */
export type AdminViewKind = 'overview' | 'table' | 'runtime' | 'settings' | 'audit'

/**
 * 管理列表中允许展示的列。
 */
export interface AdminColumn {
  /**
   * 只读字段编码，不允许密钥字段。
   */
  key: string
  /**
   * 列标题。
   */
  title: string
}

/**
 * 管理页面定义，只描述界面结构，不声明用户权限。
 */
export interface AdminView {
  /**
   * 与路由对应的唯一编码。
   */
  key: string
  /**
   * 一级模块。
   */
  section: string
  /**
   * 二级页签；没有页签时为空。
   */
  tab: string
  /**
   * 页面名称。
   */
  title: string
  /**
   * 页面用途说明。
   */
  description: string
  /**
   * 内容形式。
   */
  kind: AdminViewKind
  /**
   * 只读列表列定义。
   */
  columns: readonly AdminColumn[]
  /**
   * 待接入的管理操作名称；界面必须禁用这些操作。
   */
  actions: readonly string[]
}

/**
 * 管理后台一行只读数据。
 */
export interface AdminRow {
  /**
   * 稳定行标识。
   */
  id: string
  /**
   * 已由服务端按管理员权限过滤的字段值。
   */
  cells: Readonly<Record<string, string>>
}

/**
 * 管理工作台的指标；没有真实值时不渲染数值。
 */
export interface AdminMetric {
  /**
   * 指标编码。
   */
  key: string
  /**
   * 指标名称。
   */
  label: string
  /**
   * 由服务端返回的数值文本。
   */
  value: string
}

/**
 * 当前管理员有权查看的页面数据。
 */
export interface AdminViewData {
  /**
   * 已按权限过滤的列表行。
   */
  rows: readonly AdminRow[]
  /**
   * 已按权限过滤的工作台指标。
   */
  metrics: readonly AdminMetric[]
}

/**
 * 已评审的管理入口：列仅用于安全的只读展示，所有写入操作先禁用。
 */
export const ADMIN_VIEWS: readonly AdminView[] = [
  { key: 'overview', section: 'overview', tab: '', title: '管理工作台', description: '查看运行关注项与管理入口。', kind: 'overview', columns: [{ key: 'subject', title: '需要关注' }, { key: 'source', title: '来源' }, { key: 'updatedAt', title: '更新时间' }], actions: [] },
  { key: 'flows', section: 'flows', tab: '', title: '流程与模板', description: '查看流程、版本和发布状态。流程编辑请进入流程设计工作区。', kind: 'table', columns: [{ key: 'name', title: '流程名称' }, { key: 'code', title: '流程编码' }, { key: 'group', title: '分组' }, { key: 'version', title: '当前版本' }, { key: 'status', title: '状态' }], actions: ['新建流程', '导入流程', '停启用'] },
  { key: 'org/departments', section: 'org', tab: 'departments', title: '部门', description: '维护部门层级、负责人和分管关系。', kind: 'table', columns: [{ key: 'name', title: '部门名称' }, { key: 'code', title: '部门编码' }, { key: 'parent', title: '上级部门' }, { key: 'manager', title: '负责人' }, { key: 'source', title: '可信来源' }], actions: ['新增部门', '导入组织'] },
  { key: 'org/people', section: 'org', tab: 'people', title: '人员', description: '查看工号、归属部门、岗位和在职状态；账号操作以服务端实时校验为准。', kind: 'table', columns: [{ key: 'name', title: '姓名' }, { key: 'employeeNo', title: '工号' }, { key: 'departments', title: '所属部门' }, { key: 'position', title: '岗位' }, { key: 'status', title: '状态' }], actions: ['新增人员', '导入人员', '账号管理'] },
  { key: 'org/roles', section: 'org', tab: 'roles', title: '角色', description: '管理流程管理员与组织角色、成员和数据范围。', kind: 'table', columns: [{ key: 'name', title: '角色名称' }, { key: 'scope', title: '数据范围' }, { key: 'members', title: '成员数' }], actions: ['新增角色', '分配权限'] },
  { key: 'org/matrix', section: 'org', tab: 'matrix', title: '审批矩阵', description: '查看按金额、部门等条件配置的审批人规则。', kind: 'table', columns: [{ key: 'name', title: '规则名称' }, { key: 'condition', title: '匹配条件' }, { key: 'assignee', title: '审批人来源' }, { key: 'status', title: '状态' }], actions: ['新增规则'] },
  { key: 'org/calendar', section: 'org', tab: 'calendar', title: '工作日历', description: '维护节假日与调休，为审批时限计算提供依据。', kind: 'table', columns: [{ key: 'date', title: '日期' }, { key: 'type', title: '日期类型' }, { key: 'description', title: '说明' }], actions: ['维护日期'] },
  { key: 'apps/list', section: 'apps', tab: 'list', title: '接入应用', description: '管理接入应用、流程授权、回调、嵌入来源和业务原单。密钥不在列表中显示。', kind: 'table', columns: [{ key: 'name', title: '应用名称' }, { key: 'code', title: '应用编码' }, { key: 'flows', title: '授权流程' }, { key: 'status', title: '状态' }, { key: 'updatedAt', title: '更新时间' }], actions: ['接入向导', '新建应用', '配置嵌入来源', '配置业务原单', '轮换密钥', '启停应用'] },
  { key: 'apps/bindings', section: 'apps', tab: 'bindings', title: '业务系统绑定', description: '核对每个应用的外部用户和部门与审批服务的对应关系。', kind: 'table', columns: [{ key: 'application', title: '接入应用' }, { key: 'externalId', title: '外部 ID' }, { key: 'kind', title: '绑定类型' }, { key: 'target', title: '审批服务对象' }], actions: ['新增绑定', '自动匹配'] },
  { key: 'apps/test', section: 'apps', tab: 'test', title: '测试回调', description: '应用管理员输入已知应用编码，服务端按当前本地身份核验后排入签名测试回调。', kind: 'settings', columns: [], actions: [] },
  { key: 'runtime', section: 'runtime', tab: '', title: '运行实例', description: '查看在途审批，并预留有原因和审计的管理员干预入口。', kind: 'runtime', columns: [{ key: 'serialNo', title: '审批编号' }, { key: 'title', title: '标题' }, { key: 'flow', title: '流程' }, { key: 'status', title: '状态' }, { key: 'assignee', title: '当前处理人' }], actions: ['更换审批人', '跳转节点', '终止实例', '离职交接', '重发提醒', '重发回调'] },
  { key: 'outbound/queue', section: 'outbound', tab: 'queue', title: '待发队列', description: '查看事件投递、重试次数和失败原因；回调可按原事件 ID 重发。', kind: 'table', columns: [{ key: 'eventId', title: '事件 ID' }, { key: 'application', title: '目标应用' }, { key: 'kind', title: '类型' }, { key: 'status', title: '状态' }, { key: 'attempts', title: '尝试次数' }, { key: 'nextAttempt', title: '可发送时间' }, { key: 'lastError', title: '最近错误类别' }], actions: ['重新投递'] },
  { key: 'outbound/hooks', section: 'outbound', tab: 'hooks', title: '钩子配置', description: '查看取数、校验、取人和自动业务动作的出站配置。', kind: 'table', columns: [{ key: 'application', title: '接入应用' }, { key: 'kind', title: '钩子类型' }, { key: 'status', title: '状态' }, { key: 'timeout', title: '超时' }], actions: ['编辑钩子', '测试钩子'] },
  { key: 'reports/flow', section: 'reports', tab: 'flow', title: '流程统计', description: '查看当前管理范围内的真实流程效率与保密匿名数量。', kind: 'table', columns: [{ key: 'flow', title: '流程' }, { key: 'count', title: '发起量' }, { key: 'passRate', title: '通过率' }], actions: [] },
  { key: 'reports/node', section: 'reports', tab: 'node', title: '节点统计', description: '查看当前管理范围内的节点处理时长与超时情况。', kind: 'table', columns: [{ key: 'node', title: '节点' }, { key: 'duration', title: '平均耗时' }, { key: 'overdueRate', title: '超时率' }], actions: [] },
  { key: 'reports/people', section: 'reports', tab: 'people', title: '人员统计', description: '查看当前管理范围内公开流程的人员待办与处理量。', kind: 'table', columns: [{ key: 'person', title: '人员' }, { key: 'backlog', title: '积压待办' }, { key: 'handled', title: '处理量' }], actions: [] },
  { key: 'system/info', section: 'system', tab: 'info', title: '系统信息', description: '维护审批单打印页眉的单位名称与 Logo，所有修改均记录原因。', kind: 'settings', columns: [], actions: [] },
  { key: 'system/notifications', section: 'system', tab: 'notifications', title: '消息配置', description: '查看邮件与站内消息的渠道状态和提醒规则。', kind: 'settings', columns: [{ key: 'name', title: '配置项' }, { key: 'status', title: '状态' }], actions: ['保存配置', '发送测试邮件'] },
  { key: 'system/storage', section: 'system', tab: 'storage', title: '附件与字体', description: '查看附件存储和已授权字体配置，不回显连接密钥。', kind: 'settings', columns: [{ key: 'name', title: '配置项' }, { key: 'status', title: '状态' }], actions: ['保存配置', '上传字体'] },
  { key: 'system/security', section: 'system', tab: 'security', title: '安全设置', description: '修改当前本地管理员密码；登录方式和令牌策略配置待正式管理接口接入。', kind: 'settings', columns: [], actions: [] },
  { key: 'system/logs', section: 'system', tab: 'logs', title: '操作日志', description: '只读查看最近的组织、接入应用及回调重发变更。', kind: 'audit', columns: [{ key: 'time', title: '时间' }, { key: 'actor', title: '操作人' }, { key: 'action', title: '操作' }, { key: 'target', title: '对象' }, { key: 'reason', title: '原因' }], actions: [] },
]

/**
 * 根据当前管理路由取唯一页面定义。
 * @param section 一级模块。
 * @param tab 二级页签，没有时为空。
 * @returns 页面定义；未登记时为空。
 */
export function resolveAdminView(section: string, tab: string): AdminView | undefined {
  return ADMIN_VIEWS.find(view => view.section === section && view.tab === tab)
}

/**
 * 只在当前管理员已授权行的可见列中搜索，不读取其他字段。
 * @param rows 服务端返回的已授权行。
 * @param columns 当前页面可见列。
 * @param search 搜索词。
 * @returns 不改变原数组的筛选结果。
 */
export function filterAdminRows(rows: readonly AdminRow[], columns: readonly AdminColumn[], search: string): AdminRow[] {
  const needle = search.trim().toLocaleLowerCase()
  if (!needle) return [...rows]
  return rows.filter(row => columns.some(column => (row.cells[column.key] ?? '').toLocaleLowerCase().includes(needle)))
}

/**
 * 从只读行中提取当前列的文本，不渲染未登记或敏感字段。
 * @param row 服务端返回的已授权行。
 * @param column 经过页面登记的安全列。
 * @returns 可以直接作为文本节点渲染的值。
 */
export function adminCellText(row: AdminRow, column: AdminColumn): string {
  return row.cells[column.key] ?? '—'
}
