/**
 * 工作区名称，仅描述界面入口，不代表用户权限。
 */
export type WorkspaceKey = 'center' | 'designer' | 'admin' | 'mobile'

/**
 * 一个可导航的界面入口。
 */
export interface NavigationItem {
  /**
   * 当前工作区内唯一标识。
   */
  key: string
  /**
   * 中文名称。
   */
  title: string
  /**
   * 页面的 hash 地址。
   */
  hash: string
  /**
   * 页面用途说明。
   */
  description: string
}

/**
 * 子导航元数据，不含演示业务记录。
 */
export const SECTION_ITEMS: Readonly<Record<WorkspaceKey, readonly NavigationItem[]>> = {
  center: [
    { key: 'todo', title: '待我处理', hash: '#/', description: '集中处理分配给自己的审批任务。' },
    { key: 'done', title: '我已处理', hash: '#/center/done', description: '查看已处理的审批和操作记录。' },
    { key: 'cc', title: '抄送我的', hash: '#/center/cc', description: '查看抄送通知与相关审批进度。' },
    { key: 'started', title: '我发起的', hash: '#/center/started', description: '查看业务系统发起的审批进度。' },
    { key: 'settings', title: '个人设置', hash: '#/center/settings', description: '后续提供委托、签名、常用语和通知设置。' },
  ],
  designer: [
    { key: 'flows', title: '流程管理', hash: '#/designer', description: '维护流程定义和发布版本。' },
    { key: 'templates', title: '流程模板', hash: '#/designer/templates', description: '从模板创建流程的工作区。' },
  ],
  admin: [
    { key: 'overview', title: '管理工作台', hash: '#/admin', description: '查看管理概况和需要关注的事项。' },
    { key: 'flows', title: '流程与模板', hash: '#/admin/flows', description: '集中管理流程、分组及模板。' },
    { key: 'org', title: '组织与规则', hash: '#/admin/org', description: '维护组织、人员、角色、矩阵和日历。' },
    { key: 'apps', title: '应用接入', hash: '#/admin/apps', description: '管理接入应用与业务系统绑定。' },
    { key: 'runtime', title: '运行实例', hash: '#/admin/runtime', description: '查看审批运行情况和管理操作记录。' },
    { key: 'outbound', title: '回调与钩子', hash: '#/admin/outbound', description: '查看出站投递和钩子配置。' },
    { key: 'reports', title: '统计报表', hash: '#/admin/reports', description: '查看流程、节点和人员统计。' },
    { key: 'system', title: '系统管理', hash: '#/admin/system', description: '维护系统配置与查看操作日志。' },
  ],
  mobile: [
    { key: 'todo', title: '待办', hash: '#/m', description: '在手机上处理待办审批。' },
    { key: 'done', title: '已办', hash: '#/m/done', description: '查看自己已处理的审批。' },
    { key: 'cc', title: '抄送', hash: '#/m/cc', description: '查看抄送给自己的审批。' },
    { key: 'me', title: '我的', hash: '#/m/me', description: '个人信息与审批偏好。' },
  ],
}

/**
 * 管理页面支持的二级页签。
 */
export const ADMIN_TABS: Readonly<Record<string, readonly string[]>> = {
  org: ['departments', 'people', 'roles', 'matrix', 'calendar'], apps: ['list', 'bindings'],
  outbound: ['queue', 'hooks'], reports: ['flow', 'node', 'people'],
  system: ['info', 'notifications', 'storage', 'security', 'logs'],
}

/**
 * 二级页签名称。
 */
export const TAB_TITLES: Readonly<Record<string, string>> = {
  departments: '部门', people: '人员', roles: '角色', matrix: '审批矩阵', calendar: '工作日历',
  list: '接入应用', bindings: '业务系统绑定', queue: '待发队列', hooks: '钩子配置',
  flow: '流程统计', node: '节点统计', notifications: '消息配置', storage: '附件与字体',
  info: '系统信息', security: '安全设置', logs: '操作日志', design: '流程设计', fields: '审批数据',
  layout: '详情排版', rules: '流程规则', print: '打印设置', versions: '版本管理',
}

/**
 * 设计器工作区页签，只提供地址，不实现编辑器。
 */
export const DESIGNER_TABS = ['design', 'fields', 'layout', 'rules', 'print', 'versions'] as const
