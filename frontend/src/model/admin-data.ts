import type { AdminRow, AdminViewData } from './admin.ts'

/**
 * 管理目录接口中的部门安全摘要。
 */
export interface AdminDepartment {
  /**
   * 内部 ID。
   */
  id: string
  /**
   * 部门编码。
   */
  code: string
  /**
   * 部门名称。
   */
  name: string
  /**
   * 上级部门名称。
   */
  parentName?: string | null
  /**
   * 负责人姓名。
   */
  managerName?: string | null
  /**
   * 可信数据来源。
   */
  source: string
}

/**
 * 管理目录接口中的人员安全摘要。
 */
export interface AdminPerson {
  /**
   * 内部 ID。
   */
  id: string
  /**
   * 工号。
   */
  employeeNumber: string
  /**
   * 姓名。
   */
  name: string
  /**
   * 所属部门名称。
   */
  departments: string[]
  /**
   * 岗位。
   */
  position?: string | null
  /**
   * 在职状态。
   */
  isActive: boolean
}

/**
 * 组织目录的管理端响应。
 */
export interface AdminDirectory {
  /**
   * 部门。
   */
  departments: AdminDepartment[]
  /**
   * 人员。
   */
  people: AdminPerson[]
}

/**
 * 不含任何密钥或出站地址的应用摘要。
 */
export interface AdminClientApp {
  /**
   * 应用编码。
   */
  code: string
  /**
   * 应用名称。
   */
  name: string
  /**
   * 已授权流程编码。
   */
  allowedFlowCodes: string[]
  /**
   * 启用状态。
   */
  isEnabled: boolean
}

/**
 * 管理端可见的业务系统绑定摘要。
 */
export interface AdminBinding {
  /**
   * 接入应用编码。
   */
  clientAppCode: string
  /**
   * person 或 department。
   */
  kind: string
  /**
   * 业务系统外部 ID。
   */
  externalId: string
  /**
   * 审批服务内部 ID。
   */
  internalId: string
  /**
   * 当前内部名称。
   */
  targetName: string
  /**
   * 绑定方式。
   */
  method: string
}

/**
 * 管理端可见的操作日志元数据。
 */
export interface AdminAudit {
  /**
   * 操作时间。
   */
  occurredAt: string
  /**
   * 操作者 ID。
   */
  actorId: string
  /**
   * 操作类型。
   */
  action: string
  /**
   * 操作对象。
   */
  target: string
  /**
   * 用户填写的原因。
   */
  reason?: string | null
}

/**
 * 仅含可安全展示元数据的待发项。
 */
export interface AdminOutbox {
  /**
   * 稳定事件 ID。
   */
  eventId: string
  /**
   * 所属接入应用编码。
   */
  clientAppCode: string
  /**
   * 待发类型。
   */
  kind: string
  /**
   * 当前队列状态。
   */
  status: string
  /**
   * 已尝试次数。
   */
  attempts: number
  /**
   * 下次可发送时间。
   */
  availableAt: string
  /**
   * 最近错误类别。
   */
  errorCode?: string | null
}

/**
 * 把受保护目录响应映射到部门、人员列表已有的安全列。
 * @param viewKey 当前管理页面编码。
 * @param directory 服务端目录。
 * @returns 只含列定义允许字段的页面数据。
 */
export function directoryViewData(viewKey: string, directory: AdminDirectory): AdminViewData {
  if (viewKey === 'org/departments') {
    return {
      metrics: [],
      rows: directory.departments.map((item): AdminRow => ({
        id: item.id,
        cells: {
          code: item.code, name: item.name, parent: item.parentName ?? '—',
          manager: item.managerName ?? '—', source: item.source,
        },
      })),
    }
  }
  if (viewKey === 'org/people') {
    return {
      metrics: [],
      rows: directory.people.map((item): AdminRow => ({
        id: item.id,
        cells: {
          name: item.name, employeeNo: item.employeeNumber,
          departments: item.departments.join('、') || '—', position: item.position ?? '—',
          status: item.isActive ? '在职' : '离职',
        },
      })),
    }
  }
  throw new Error('不支持的组织管理页面。')
}

/**
 * 把接入应用安全摘要映射为列表，不访问密钥字段。
 * @param apps 管理员已授权的应用摘要。
 * @returns 页面可展示的安全行。
 */
export function clientAppsViewData(apps: readonly AdminClientApp[]): AdminViewData {
  return {
    metrics: [],
    rows: apps.map((item): AdminRow => ({
      id: item.code,
      cells: {
        name: item.name, code: item.code, flows: item.allowedFlowCodes.join('、') || '—',
        status: item.isEnabled ? '启用' : '停用', updatedAt: '—',
      },
    })),
  }
}

/**
 * 将绑定摘要转换为安全列表行。
 * @param bindings 服务端已授权的绑定列表。
 * @returns 只含列定义的管理行。
 */
export function bindingsViewData(bindings: readonly AdminBinding[]): AdminViewData {
  return {
    metrics: [],
    rows: bindings.map((item): AdminRow => ({
      id: `${item.clientAppCode}:${item.kind}:${item.externalId}`,
      cells: {
        application: item.clientAppCode, externalId: item.externalId,
        kind: item.kind === 'person' ? '人员' : '部门',
        target: `${item.targetName}（${item.internalId}）`,
      },
    })),
  }
}

/**
 * 将审计元数据转换为只读列表，不展示原始变更快照。
 * @param audit 服务端已授权的审计列表。
 * @returns 只含安全列的管理行。
 */
export function auditViewData(audit: readonly AdminAudit[]): AdminViewData {
  return {
    metrics: [],
    rows: audit.map((item, index): AdminRow => ({
      id: `${item.occurredAt}:${index}`,
      cells: {
        time: new Date(item.occurredAt).toLocaleString('zh-CN'), actor: item.actorId,
        action: auditActionLabel(item.action), target: auditTargetLabel(item.target),
        reason: item.reason || '—',
      },
    })),
  }
}

/**
 * 将已知审计动作编码转换为用户能直接理解的中文。
 * @param action 后端保存的审计动作编码。
 * @returns 中文操作名称；未知编码原样展示，避免丢失证据。
 */
function auditActionLabel(action: string): string {
  return ({ created: '新增', create: '新增', updated: '修改', update: '修改',
    embedOriginsUpdated: '修改允许嵌入来源', secretsRotated: '轮换密钥',
    enabledChanged: '启停应用', retried: '手动重发', companyName: '修改单位名称',
    logoUpload: '上传单位 Logo', logoDelete: '移除单位 Logo' } as Record<string, string>)[action] ?? action
}

/**
 * 将已知审计对象类型转换为中文，同时保留原始对象 ID。
 * @param target 对象类型与 ID 的组合。
 * @returns 用户可读的对象名称。
 */
function auditTargetLabel(target: string): string {
  const separator = target.indexOf(':')
  if (separator < 0) return target
  const kind = target.slice(0, separator)
  const label = ({ person: '人员', department: '部门', membership: '部门归属',
    personBinding: '人员绑定', departmentBinding: '部门绑定', clientApp: '接入应用', callback: '回调',
    systemBranding: '打印页眉品牌',
    'person-role': '人员角色' } as Record<string, string>)[kind] ?? kind
  return `${label}：${target.slice(separator + 1)}`
}

/**
 * 将待发项元数据转换为队列列表，不读取正文和目标地址。
 * @param outbox 管理员有权查看的待发项摘要。
 * @returns 页面可展示的安全行。
 */
export function outboxViewData(outbox: readonly AdminOutbox[]): AdminViewData {
  return {
    metrics: [],
    rows: outbox.map((item, index): AdminRow => ({
      id: `${item.eventId}:${index}`,
      cells: {
        eventId: item.eventId, application: item.clientAppCode,
        kind: item.kind === 'callback' ? '回调' : item.kind,
        status: ({ pending: '待发送', leased: '发送中', completed: '已完成' } as Record<string, string>)[item.status] ?? item.status,
        attempts: String(item.attempts), nextAttempt: new Date(item.availableAt).toLocaleString('zh-CN'),
        lastError: item.errorCode || '—',
      },
    })),
  }
}
