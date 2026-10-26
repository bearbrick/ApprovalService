import type { AdminViewData } from '../model/admin.ts'
import { auditViewData, bindingsViewData, clientAppsViewData, directoryViewData, outboxViewData } from '../model/admin-data.ts'
import type { AdminAudit, AdminBinding, AdminClientApp, AdminDirectory, AdminOutbox, AdminPerson } from '../model/admin-data.ts'
import { ApiError, getJson, requestJson } from './http.ts'
import { adminAccessToken } from './admin-session.ts'

/**
 * 尚未实现的管理模块保持明确状态。
 */
export class AdminAdapterUnavailableError extends Error {
  /**
   * 构造页面可辨认的未接入错误。
   */
  constructor() {
    super('此管理页面的数据接口尚未接入。')
    this.name = 'AdminAdapterUnavailableError'
  }
}

/**
 * 管理页面只读数据适配入口，所有请求附带已核对管理员令牌。
 */
export interface AdminDataAdapter {
  /**
   * 读取当前登录管理员有权查看的页面数据。
   * @param viewKey 管理页面编码。
   * @param signal 路由切换或重试的取消信号。
   * @returns 按权限过滤后的列表与指标。
   */
  loadView(viewKey: string, signal: AbortSignal): Promise<AdminViewData>
}

/**
 * 对已落地的管理读取端点发起请求；其它页面不伪造数据。
 */
export const adminAdapter: AdminDataAdapter = {
  /**
   * @inheritdoc
   */
  async loadView(viewKey: string, signal: AbortSignal): Promise<AdminViewData> {
    if (!['overview', 'org/departments', 'org/people', 'apps/list', 'apps/bindings', 'system/logs', 'system/security', 'system/info', 'outbound/queue'].includes(viewKey)) {
      throw new AdminAdapterUnavailableError()
    }
    const token = await adminAccessToken()
    const options = { headers: { Authorization: `Bearer ${token}` }, signal, cache: 'no-store' as RequestCache }
    if (viewKey === 'overview' || viewKey === 'system/security' || viewKey === 'system/info') {
      await getJson<{ personId: string }>('/api/v1/admin/session', options)
      return { rows: [], metrics: [] }
    }
    if (viewKey === 'apps/list') {
      const apps = await getJson<AdminClientApp[]>('/api/v1/admin/apps', options)
      if (!Array.isArray(apps) || apps.some(item => !item || typeof item.code !== 'string'
        || typeof item.name !== 'string' || !Array.isArray(item.allowedFlowCodes)
        || typeof item.isEnabled !== 'boolean')) throw new Error('接入应用列表响应不完整。')
      return clientAppsViewData(apps)
    }
    if (viewKey === 'apps/bindings') {
      const bindings = await getJson<AdminBinding[]>('/api/v1/admin/bindings', options)
      if (!Array.isArray(bindings) || bindings.some(item => !item || typeof item.clientAppCode !== 'string'
        || typeof item.kind !== 'string' || typeof item.externalId !== 'string'
        || typeof item.internalId !== 'string' || typeof item.targetName !== 'string')) {
        throw new Error('业务系统绑定响应不完整。')
      }
      return bindingsViewData(bindings)
    }
    if (viewKey === 'system/logs') {
      const audit = await getJson<AdminAudit[]>('/api/v1/admin/audit', options)
      if (!Array.isArray(audit) || audit.some(item => !item || typeof item.occurredAt !== 'string'
        || typeof item.actorId !== 'string' || typeof item.action !== 'string'
        || typeof item.target !== 'string')) throw new Error('操作日志响应不完整。')
      return auditViewData(audit)
    }
    if (viewKey === 'outbound/queue') {
      const outbox = await getJson<AdminOutbox[]>('/api/v1/admin/outbox', options)
      if (!Array.isArray(outbox) || outbox.some(item => !item || typeof item.eventId !== 'string'
        || typeof item.clientAppCode !== 'string' || typeof item.kind !== 'string'
        || typeof item.status !== 'string' || typeof item.attempts !== 'number'
        || typeof item.availableAt !== 'string')) throw new Error('待发队列响应不完整。')
      return outboxViewData(outbox)
    }
    const directory = await getJson<AdminDirectory>('/api/v1/admin/org/directory', options)
    if (!directory || !Array.isArray(directory.departments) || !Array.isArray(directory.people)) {
      throw new Error('组织目录响应不完整。')
    }
    return directoryViewData(viewKey, directory)
  },
}

/**
 * 以原始事件 ID 手动重发回调并提交管理员填写的原因。
 * @param eventId 原始回调事件 ID。
 * @param reason 本次重发原因。
 * @returns 重排完成。
 */
export async function retryAdminCallback(eventId: string, reason: string): Promise<void> {
  const token = await adminAccessToken()
  await requestJson<void>(`/api/v1/admin/callbacks/${encodeURIComponent(eventId)}/retry`, {
    method: 'POST', headers: { Authorization: `Bearer ${token}` },
    body: { reason }, cache: 'no-store',
  })
}

/**
 * 在已授权的管理页面中手工建立业务系统外部标识绑定。
 * @param clientAppCode 接入应用编码。
 * @param kind 人员或部门。
 * @param externalId 业务系统外部标识。
 * @param internalId 审批服务内部标识。
 * @returns 保存完成。
 */
export async function saveAdminBinding(clientAppCode: string, kind: 'person' | 'department',
  externalId: string, internalId: string): Promise<void> {
  const token = await adminAccessToken()
  await requestJson<unknown>('/api/v1/admin/org/bindings', {
    method: 'POST', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
    body: { kind, externalId, internalId, employeeNumber: null, phone: null, method: 'manual', clientAppCode },
  })
}

/**
 * 加载绑定编辑器的应用与内部目标选项，两个查询都由管理员权限保护。
 * @returns 可选择的应用和组织目录。
 */
export async function loadAdminBindingOptions(): Promise<{ apps: AdminClientApp[]; directory: AdminDirectory }> {
  const token = await adminAccessToken()
  const options = { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' as RequestCache }
  const [apps, directory] = await Promise.all([
    getJson<AdminClientApp[]>('/api/v1/admin/apps', options),
    getJson<AdminDirectory>('/api/v1/admin/org/directory', options),
  ])
  if (!Array.isArray(apps) || !directory || !Array.isArray(directory.people)
    || !Array.isArray(directory.departments)) throw new Error('绑定选项响应不完整。')
  return { apps, directory }
}

/**
 * 从受保护组织目录读取账号管理可选择的人员，不暴露密码或令牌。
 * @returns 当前组织人员的安全摘要。
 */
export async function loadAdminAccountPeople(): Promise<AdminPerson[]> {
  const token = await adminAccessToken()
  const directory = await getJson<AdminDirectory>('/api/v1/admin/org/directory', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  if (!directory || !Array.isArray(directory.people) || directory.people.some(person => !person
    || typeof person.id !== 'string' || typeof person.employeeNumber !== 'string'
    || typeof person.name !== 'string' || typeof person.isActive !== 'boolean')) {
    throw new Error('组织人员响应不完整。')
  }
  return directory.people
}

/**
 * 为已有在职人员开通本地账号，临时密码只随本次请求发送。
 * @param personId 内部人员 ID。
 * @param temporaryPassword 首次登录专用的临时密码。
 * @returns 保存完成。
 */
export async function provisionAdminAccount(personId: string, temporaryPassword: string): Promise<void> {
  const token = await adminAccessToken()
  await requestJson<void>('/api/v1/identity/provision', {
    method: 'POST', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
    body: { personId, temporaryPassword },
  })
}

/**
 * 作废人员目前的访问与刷新令牌，账号和密码仍可继续使用。
 * @param personId 内部人员 ID。
 * @returns 作废完成。
 */
export async function revokeAdminSessions(personId: string): Promise<void> {
  const token = await adminAccessToken()
  await requestJson<void>('/api/v1/identity/revoke-all', {
    method: 'POST', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
    body: { personId },
  })
}

/**
 * 接入应用首次登记的资料，密钥由服务端生成。
 */
export interface AdminClientAppCreateInput {
  /**
   * 唯一应用编码。
   */
  code: string
  /**
   * 显示名称。
   */
  name: string
  /**
   * 流程授权编码。
   */
  allowedFlowCodes: string[]
  /**
   * 是否允许跨应用统计待办。
   */
  canViewAllTasks: boolean
  /**
   * 回调与四类业务钩子地址。
   */
  callbackUrl: string | null
  /**
   * 取数地址。
   */
  dataUrl: string | null
  /**
   * 校验地址。
   */
  validationUrl: string | null
  /**
   * 取人地址。
   */
  assigneeUrl: string | null
  /**
   * 自动动作地址。
   */
  actionUrl: string | null
  /**
   * 管理员填写的变更原因。
   */
  reason: string
}

/**
 * 只在创建或轮换响应中展示的临时凭据。
 */
export interface AdminClientAppSecrets {
  /**
   * 应用编码。
   */
  code: string
  /**
   * 新 API Key。
   */
  apiKey: string
  /**
   * 新签名密钥。
   */
  signingSecret: string
}

/**
 * 管理员登记应用并仅接收本次新生成的凭据。
 * @param input 应用非密钥资料。
 * @returns 本次生成的 API Key 和签名密钥。
 */
export async function createAdminClientApp(input: AdminClientAppCreateInput): Promise<AdminClientAppSecrets> {
  const token = await adminAccessToken()
  const result = await requestJson<AdminClientAppSecrets>('/api/v1/admin/apps', {
    method: 'POST', headers: { Authorization: `Bearer ${token}` }, body: input, cache: 'no-store',
  })
  if (!result || result.code !== input.code || typeof result.apiKey !== 'string'
    || typeof result.signingSecret !== 'string') throw new Error('接入应用创建响应不完整。')
  return result
}

/**
 * 管理员轮换已有应用密钥，旧 API Key 与嵌入会话随即失效。
 * @param code 接入应用编码。
 * @param reason 轮换原因。
 * @returns 只显示一次的新凭据。
 */
export async function rotateAdminClientAppSecrets(code: string, reason: string): Promise<AdminClientAppSecrets> {
  const token = await adminAccessToken()
  const result = await requestJson<AdminClientAppSecrets>(`/api/v1/admin/apps/${encodeURIComponent(code)}/rotate-secrets`, {
    method: 'POST', headers: { Authorization: `Bearer ${token}` }, body: { reason }, cache: 'no-store',
  })
  if (!result || result.code !== code || typeof result.apiKey !== 'string'
    || typeof result.signingSecret !== 'string') throw new Error('密钥轮换响应不完整。')
  return result
}

/**
 * 管理员带原因启用或停用接入应用。
 * @param code 接入应用编码。
 * @param enabled 目标启用状态。
 * @param reason 变更原因。
 * @returns 保存完成。
 */
export async function setAdminClientAppEnabled(code: string, enabled: boolean, reason: string): Promise<void> {
  const token = await adminAccessToken()
  await requestJson<void>(`/api/v1/admin/apps/${encodeURIComponent(code)}/enabled`, {
    method: 'PUT', headers: { Authorization: `Bearer ${token}` }, body: { enabled, reason }, cache: 'no-store',
  })
}

/**
 * 组织导入预检逐项问题。
 */
export interface OrganizationImportIssue {
  /**
   * 文件中的字段位置。
   */
  field: string
  /**
   * 中文问题说明。
   */
  message: string
}

/**
 * 组织文件预检或正式导入结果。
 */
export interface OrganizationImportResult {
  /**
   * 正式导入成功时为真。
   */
  imported: boolean
  /**
   * 校验问题。
   */
  errors: OrganizationImportIssue[]
}

/**
 * 以管理员令牌上传组织文件，不覆盖浏览器生成的 multipart 边界。
 * @param file 选中的 CSV 或 XLSX 文件。
 * @param preview 仅预检查时为真。
 * @param signal 页面离开时的取消信号。
 * @returns 预检问题或导入结果。
 */
export async function uploadOrganizationFile(file: File, preview: boolean, signal?: AbortSignal): Promise<OrganizationImportResult> {
  const token = await adminAccessToken()
  const form = new FormData()
  form.append('file', file, file.name)
  const response = await fetch(`/api/v1/admin/org/import?preview=${preview}`, {
    method: 'POST', headers: { Authorization: `Bearer ${token}` }, body: form, signal, cache: 'no-store',
  })
  const body: unknown = await response.json()
  if (!response.ok) {
    const error = body && typeof body === 'object' ? body as Record<string, unknown> : {}
    throw new ApiError(typeof error.code === 'string' ? error.code : `HTTP_${response.status}`,
      typeof error.message === 'string' ? error.message : '组织文件上传失败。',
      typeof error.traceId === 'string' ? error.traceId : '', { status: response.status })
  }
  if (!body || typeof body !== 'object' || !('errors' in body) || !Array.isArray(body.errors)) {
    throw new Error('组织导入响应不完整。')
  }
  const result = body as { imported?: unknown; errors: unknown[] }
  return {
    imported: result.imported === true,
    errors: result.errors.filter((issue): issue is OrganizationImportIssue => !!issue
      && typeof issue === 'object' && 'field' in issue && 'message' in issue
      && typeof issue.field === 'string' && typeof issue.message === 'string'),
  }
}

/**
 * 通过受保护的管理接口下载组织导入模板。
 * @returns Excel 模板字节。
 */
export async function downloadOrganizationTemplate(): Promise<Blob> {
  const token = await adminAccessToken()
  const response = await fetch('/api/v1/admin/org/template?format=xlsx', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  if (!response.ok) throw new ApiError(`HTTP_${response.status}`, '组织模板下载失败。', '', { status: response.status })
  return response.blob()
}

/**
 * 接入应用允许嵌入的可信网站来源。
 */
export interface AdminEmbedOrigins {
  /**
   * 接入应用编码。
   */
  code: string
  /**
   * 完整来源列表，不包含通配符。
   */
  origins: string[]
}

/**
 * 读取指定应用允许嵌入的网站来源。
 * @param code 接入应用编码。
 * @returns 当前来源设置。
 */
export async function getAdminEmbedOrigins(code: string): Promise<AdminEmbedOrigins> {
  const token = await adminAccessToken()
  const result = await getJson<AdminEmbedOrigins>(`/api/v1/admin/apps/${encodeURIComponent(code)}/embed-origins`, {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  if (!result || result.code !== code || !Array.isArray(result.origins)
    || result.origins.some(origin => typeof origin !== 'string')) throw new Error('允许嵌入来源响应不完整。')
  return result
}

/**
 * 保存指定应用的允许嵌入来源，并提供必填的管理操作原因。
 * @param code 接入应用编码。
 * @param origins 完整可信来源列表。
 * @param reason 本次变更原因。
 * @returns 服务端重新读取的设置。
 */
export async function saveAdminEmbedOrigins(code: string, origins: readonly string[], reason: string): Promise<AdminEmbedOrigins> {
  const token = await adminAccessToken()
  const result = await requestJson<AdminEmbedOrigins>(`/api/v1/admin/apps/${encodeURIComponent(code)}/embed-origins`, {
    method: 'PUT', headers: { Authorization: `Bearer ${token}` }, body: { origins, reason }, cache: 'no-store',
  })
  if (!result || result.code !== code || !Array.isArray(result.origins)) throw new Error('来源保存响应不完整。')
  return result
}
