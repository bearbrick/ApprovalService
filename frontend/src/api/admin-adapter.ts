import type { AdminViewData } from '../model/admin.ts'
import { auditViewData, bindingsViewData, clientAppsViewData, directoryViewData, outboxViewData } from '../model/admin-data.ts'
import type { AdminAudit, AdminBinding, AdminClientApp, AdminDirectory, AdminOutbox } from '../model/admin-data.ts'
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
    if (!['overview', 'org/departments', 'org/people', 'apps/list', 'apps/bindings', 'system/logs', 'outbound/queue'].includes(viewKey)) {
      throw new AdminAdapterUnavailableError()
    }
    const token = await adminAccessToken()
    const options = { headers: { Authorization: `Bearer ${token}` }, signal, cache: 'no-store' as RequestCache }
    if (viewKey === 'overview') {
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
