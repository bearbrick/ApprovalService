import type { FlowDefinition } from '../model/flow.ts'
import { ApiError, getJson, requestJson } from './http.ts'
import { adminAccessToken } from './admin-session.ts'

/**
 * 管理范围内的流程摘要，详情读取时才包含草稿。
 */
export interface ManagedFlow {
  /**
   * 稳定流程编码。
   */
  code: string
  /**
   * 流程名称。
   */
  name: string
  /**
   * 流程分组。
   */
  group: string | null
  /**
   * 直属所属部门内部 ID。
   */
  ownerDepartmentId: string | null
  /**
   * 运行期指定管理员内部人员 ID。
   */
  administratorId: string | null
  /**
   * 是否允许新发起。
   */
  isEnabled: boolean
  /**
   * 当前草稿修订。
   */
  draftRevision: number
  /**
   * 详情中的完整草稿；历史流程可能没有草稿。
   */
  draft: FlowDefinition | null
  /**
   * 当前发布版 ID。
   */
  publishedVersionId: string | null
  /**
   * 当前发布版本号。
   */
  publishedVersion: number | null
}

/**
 * 创建或保存草稿时一并提交的元数据。
 */
export interface FlowMetadata {
  /**
   * 分组。
   */
  group: string | null
  /**
   * 直属所属部门。
   */
  ownerDepartmentId: string | null
  /**
   * 运行期指定管理员。
   */
  administratorId: string | null
}

/**
 * 服务端对兼容 BPMN 文件的只读预检。
 */
export interface FlowImportPreview {
  /**
   * 文件内流程编码。
   */
  code: string
  /**
   * 文件内流程名称。
   */
  name: string
  /**
   * 图中节点数。
   */
  nodeCount: number
  /**
   * 是否存在同编码流程。
   */
  hasCodeConflict: boolean
  /**
   * 当前人员是否有权覆盖。
   */
  canOverwrite: boolean
  /**
   * 可覆盖时的当前草稿修订。
   */
  existingDraftRevision: number | null
  /**
   * 导入后发布仍需解决的问题。
   */
  publishIssues: readonly { elementId: string | null; message: string }[]
}

/**
 * 服务端管理路由前缀。
 */
const path = '/api/v1/admin/flows'

/**
 * 判断 JSON 值是否为普通对象。
 * @param value 服务响应。
 * @returns 可读取属性时为真。
 */
function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * 只接收完整的流程管理摘要，避免损坏响应进入设计器。
 * @param value 服务响应中的单个流程。
 * @returns 契约完整时为真。
 */
function managedFlow(value: unknown): value is ManagedFlow {
  if (!record(value)) return false
  return typeof value.code === 'string' && !!value.code && typeof value.name === 'string'
    && typeof value.isEnabled === 'boolean' && typeof value.draftRevision === 'number'
    && Number.isSafeInteger(value.draftRevision)
    && value.draftRevision >= 0 && (value.group == null || typeof value.group === 'string')
    && (value.ownerDepartmentId == null || typeof value.ownerDepartmentId === 'string')
    && (value.administratorId == null || typeof value.administratorId === 'string')
    && (value.publishedVersionId == null || typeof value.publishedVersionId === 'string')
    && (value.publishedVersion == null || Number.isSafeInteger(value.publishedVersion))
    && (value.draft == null || record(value.draft) && typeof value.draft.code === 'string'
      && typeof value.draft.name === 'string' && record(value.draft.canvas) && record(value.draft.data))
}

/**
 * 要求服务端返回的流程结构完整。
 * @param value 未经验证的响应。
 * @returns 已验证的流程。
 */
function requireFlow(value: unknown): ManagedFlow {
  if (!managedFlow(value)) throw new Error('流程管理响应不完整，请刷新后重试。')
  return { ...value, group: value.group ?? null, ownerDepartmentId: value.ownerDepartmentId ?? null,
    administratorId: value.administratorId ?? null, draft: value.draft ?? null,
    publishedVersionId: value.publishedVersionId ?? null, publishedVersion: value.publishedVersion ?? null }
}

/**
 * 生成当前流程管理会话的请求头。
 * @returns JWT 请求头。
 */
async function headers(): Promise<HeadersInit> {
  return { Authorization: `Bearer ${await adminAccessToken('flows')}` }
}

/**
 * 列出当前人员获授权管理的流程。
 * @param signal 页面切换时的取消标记。
 * @returns 服务端过滤后的流程摘要。
 */
export async function listManagedFlows(signal?: AbortSignal): Promise<ManagedFlow[]> {
  const value = await getJson<unknown>(path, { headers: await headers(), signal, cache: 'no-store' })
  if (!Array.isArray(value) || !value.every(managedFlow)) throw new Error('流程目录响应不完整，请刷新后重试。')
  return value.map(requireFlow)
}

/**
 * 读取有权管理的流程草稿和发布指针。
 * @param code 流程编码。
 * @param signal 页面切换时的取消标记。
 * @returns 完整管理详情。
 */
export async function getManagedFlow(code: string, signal?: AbortSignal): Promise<ManagedFlow> {
  const value = await getJson<unknown>(`${path}/${encodeURIComponent(code)}`,
    { headers: await headers(), signal, cache: 'no-store' })
  return requireFlow(value)
}

/**
 * 建立一份从未发布的可编辑草稿。
 * @param definition 完整自由画布定义。
 * @param metadata 流程归属和分组。
 * @param reason 必填审计原因。
 * @returns 服务端创建的流程详情。
 */
export async function createManagedFlow(definition: FlowDefinition, metadata: FlowMetadata,
  reason: string): Promise<ManagedFlow> {
  const value = await requestJson<unknown>(path, { method: 'POST', headers: await headers(),
    body: { definition, ...metadata, reason }, cache: 'no-store' })
  return requireFlow(value)
}

/**
 * 带预期修订号替换服务端草稿。
 * @param code 稳定流程编码。
 * @param revision 上次读取的修订号。
 * @param definition 完整新草稿。
 * @param metadata 可编辑元数据。
 * @param reason 必填审计原因。
 * @returns 新的服务端修订。
 */
export async function saveManagedFlow(code: string, revision: number, definition: FlowDefinition,
  metadata: FlowMetadata, reason: string): Promise<ManagedFlow> {
  const value = await requestJson<unknown>(`${path}/${encodeURIComponent(code)}/draft`, {
    method: 'PUT', headers: await headers(), body: { expectedRevision: revision, definition, ...metadata, reason },
    cache: 'no-store',
  })
  return requireFlow(value)
}

/**
 * 同时核对草稿与当前发布版后创建新的不可变版本。
 * @param code 流程编码。
 * @param revision 当前草稿修订。
 * @param versionId 当前发布版 ID，首次发布为空。
 * @param reason 发布审计原因。
 * @returns 新的发布指针。
 */
export async function publishManagedFlow(code: string, revision: number, versionId: string | null,
  reason: string): Promise<ManagedFlow> {
  const value = await requestJson<unknown>(`${path}/${encodeURIComponent(code)}/publish`, {
    method: 'POST', headers: await headers(), body: { expectedRevision: revision,
      expectedPublishedVersionId: versionId, reason }, cache: 'no-store',
  })
  return requireFlow(value)
}

/**
 * 带预期状态停用或恢复流程的新发起入口。
 * @param flow 上次读取的流程摘要。
 * @param enabled 目标启用状态。
 * @param reason 审计原因。
 * @returns 最新流程摘要。
 */
export async function setManagedFlowEnabled(flow: ManagedFlow, enabled: boolean,
  reason: string): Promise<ManagedFlow> {
  const value = await requestJson<unknown>(`${path}/${encodeURIComponent(flow.code)}/enabled`, {
    method: 'PUT', headers: await headers(), body: { expectedEnabled: flow.isEnabled, enabled, reason },
    cache: 'no-store',
  })
  return requireFlow(value)
}

/**
 * 只预检本服务兼容 BPMN 内容，不修改草稿。
 * @param bpmnXml 文件 UTF-8 文本。
 * @returns 冲突、覆盖范围和发布问题。
 */
export async function previewManagedImport(bpmnXml: string): Promise<FlowImportPreview> {
  const value = await requestJson<unknown>(`${path}/import/preview`, {
    method: 'POST', headers: await headers(), body: { bpmnXml }, cache: 'no-store',
  })
  if (!record(value) || typeof value.code !== 'string' || typeof value.name !== 'string'
    || !Number.isSafeInteger(value.nodeCount) || typeof value.hasCodeConflict !== 'boolean'
    || typeof value.canOverwrite !== 'boolean' || !Array.isArray(value.publishIssues)
    || (value.existingDraftRevision != null && !Number.isSafeInteger(value.existingDraftRevision))
    || !value.publishIssues.every((issue: unknown) => record(issue) && typeof issue.message === 'string'
      && (issue.elementId == null || typeof issue.elementId === 'string'))) {
    throw new Error('BPMN 预检响应不完整，请重试。')
  }
  return { ...value, existingDraftRevision: value.existingDraftRevision ?? null,
    publishIssues: (value.publishIssues as Record<string, unknown>[]).map(issue => ({
      elementId: issue.elementId ?? null, message: issue.message,
    })) } as unknown as FlowImportPreview
}

/**
 * 按预检结果创建新草稿，或显式覆盖目标的预期修订。
 * @param bpmnXml 已预检的原文件。
 * @param metadata 新建时的归属；覆盖时空值保留旧值。
 * @param reason 审计原因。
 * @param targetCode 显式覆盖目标；新建时不传。
 * @param expectedRevision 覆盖目标的预期修订。
 * @returns 保存后的服务端流程详情。
 */
export async function importManagedFlow(bpmnXml: string, metadata: FlowMetadata, reason: string,
  targetCode?: string, expectedRevision?: number): Promise<ManagedFlow> {
  const value = await requestJson<unknown>(`${path}/import`, {
    method: 'POST', headers: await headers(), body: { bpmnXml, ...metadata, reason,
      targetCode: targetCode ?? null, expectedRevision: expectedRevision ?? null }, cache: 'no-store',
  })
  return requireFlow(value)
}

/**
 * 导出当前草稿或指定不可变发布版的服务兼容 BPMN 文件。
 * @param code 流程编码。
 * @param source 草稿或发布版。
 * @param version 指定发布版本号，仅发布版使用。
 * @returns UTF-8 XML 文本。
 */
export async function exportManagedFlow(code: string, source: 'draft' | 'published',
  version?: number): Promise<string> {
  const token = await adminAccessToken('flows')
  const query = new URLSearchParams({ source })
  if (version !== undefined) query.set('version', String(version))
  let response: Response
  try {
    response = await fetch(`${path}/${encodeURIComponent(code)}/export?${query}`, {
      headers: { Authorization: `Bearer ${token}`, Accept: 'application/xml' }, cache: 'no-store',
    })
  } catch {
    throw new ApiError('NETWORK_ERROR', '无法连接审批流服务，请检查网络后重试。', '', { kind: 'network' })
  }
  if (!response.ok) {
    let message = `导出失败（HTTP ${response.status}）。`
    try {
      const body: unknown = await response.json()
      if (record(body) && typeof body.message === 'string') message = body.message
    } catch { /* 错误网关可能返回非 JSON 页面。 */ }
    throw new ApiError('HTTP_' + response.status, message, '', { status: response.status })
  }
  if (response.headers.get('Content-Type')?.split(';')[0]?.trim().toLowerCase() !== 'application/xml') {
    throw new ApiError('INVALID_RESPONSE', '服务返回的 BPMN 文件类型不正确。', '',
      { status: response.status, kind: 'invalid-response' })
  }
  const xml = await response.text()
  if (!xml.trim() || xml.length > 4 * 1024 * 1024 || !xml.includes('<')) {
    throw new ApiError('INVALID_RESPONSE', '服务返回的 BPMN 文件内容不正确。', '',
      { status: response.status, kind: 'invalid-response' })
  }
  return xml
}
