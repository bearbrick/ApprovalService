import { getJson, requestJson } from './http.ts'
import { adminAccessToken } from './admin-session.ts'

/**
 * 向导直接从服务端读取的应用摘要，保留原始流程编码数组。
 */
export interface WizardApp {
  /**
   * 应用编码。
   */
  code: string
  /**
   * 应用名称。
   */
  name: string
  /**
   * 是否启用。
   */
  isEnabled: boolean
  /**
   * 已授权的原始流程编码。
   */
  flowCodes: string[]
}

/**
 * 组织管理员读取安全应用摘要，保留流程编码原值供示例代码生成。
 * @returns 当前接入应用列表。
 */
export async function getWizardApps(): Promise<WizardApp[]> {
  const token = await adminAccessToken()
  const result = await getJson<Array<{ code: string; name: string; isEnabled: boolean; allowedFlowCodes: string[] }>>(
    '/api/v1/admin/apps', { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' })
  if (!Array.isArray(result) || result.some(item => !item || typeof item.code !== 'string'
    || typeof item.name !== 'string' || typeof item.isEnabled !== 'boolean'
    || !Array.isArray(item.allowedFlowCodes) || item.allowedFlowCodes.some(code => typeof code !== 'string'))) {
    throw new Error('接入应用列表响应不完整。')
  }
  return result.map(item => ({ code: item.code, name: item.name, isEnabled: item.isEnabled,
    flowCodes: item.allowedFlowCodes }))
}

/**
 * 接入应用管理员名单的安全响应。
 */
export interface AppAdministrators {
  /**
   * 目标应用编码。
   */
  code: string
  /**
   * 内部人员 ID，不包含姓名或凭据。
   */
  personIds: string[]
}

/**
 * 一次测试回调的排队结果。
 */
export interface TestCallbackReceipt {
  /**
   * 可在组织管理员待发队列查找的事件 ID。
   */
  eventId: string
  /**
   * 当前仅可能是排队状态。
   */
  status: 'queued'
}

/**
 * 读取一个应用的管理员名单，服务端仅允许组织管理员。
 * @param code 应用编码。
 * @returns 当前名单和应用编码。
 */
export async function getAppAdministrators(code: string): Promise<AppAdministrators> {
  const token = await adminAccessToken()
  const result = await getJson<AppAdministrators>(`/api/v1/admin/apps/${encodeURIComponent(code)}/administrators`, {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  if (!result || result.code !== code || !Array.isArray(result.personIds)
    || result.personIds.some(id => typeof id !== 'string')) throw new Error('应用管理员名单响应不完整。')
  return result
}

/**
 * 按已读取的旧名单条件保存新名单，冲突交给操作者刷新。
 * @param code 应用编码。
 * @param expectedPersonIds 读取时的旧名单。
 * @param personIds 新名单。
 * @param reason 变更原因。
 * @returns 服务端确认的新名单。
 */
export async function saveAppAdministrators(code: string, expectedPersonIds: readonly string[],
  personIds: readonly string[], reason: string): Promise<AppAdministrators> {
  const token = await adminAccessToken()
  const result = await requestJson<AppAdministrators>(`/api/v1/admin/apps/${encodeURIComponent(code)}/administrators`, {
    method: 'PUT', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
    body: { expectedPersonIds, personIds, reason },
  })
  if (!result || result.code !== code || !Array.isArray(result.personIds)) throw new Error('应用管理员名单保存响应不完整。')
  return result
}

/**
 * 仅凭当前本地身份和已知应用编码排入签名测试回调。
 * @param code 应用编码。
 * @param reason 可审计的测试原因。
 * @returns 排队事件 ID；实际投递由持久队列异步完成。
 */
export async function queueTestCallback(code: string, reason: string): Promise<TestCallbackReceipt> {
  const token = await adminAccessToken('app-test')
  const result = await requestJson<TestCallbackReceipt>(`/api/v1/admin/apps/${encodeURIComponent(code)}/test-callback`, {
    method: 'POST', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store', body: { reason },
  })
  if (!result || typeof result.eventId !== 'string' || !result.eventId.startsWith('test-')
    || result.status !== 'queued') throw new Error('测试回调响应不完整。')
  return result
}
