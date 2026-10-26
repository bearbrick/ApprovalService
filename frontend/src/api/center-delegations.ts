import { getJson, requestJson } from './http.ts'
import { centerAccessToken } from './center-session.ts'

/**
 * 当前人员的一项持久委托规则。
 */
export interface CenterDelegation {
  /**
   * 规则 ID。
   */
  id: string
  /**
   * 委托人的内部人员 ID。
   */
  principalPersonId: string
  /**
   * 代理人的内部人员 ID。
   */
  delegatePersonId: string
  /**
   * UTC 生效起点。
   */
  startsAtUtc: string
  /**
   * UTC 结束时刻。
   */
  endsAtUtc: string
  /**
   * 限定流程编码，空列表代表全部。
   */
  flowCodes: string[]
}

/**
 * 新建委托的输入。
 */
export interface CenterDelegationInput {
  /**
   * 代理人的内部人员 ID。
   */
  delegatePersonId: string
  /**
   * UTC 生效起点。
   */
  startsAtUtc: string
  /**
   * UTC 结束时刻。
   */
  endsAtUtc: string
  /**
   * 限定流程编码，空列表代表全部。
   */
  flowCodes: string[]
}

/**
 * 已有任务逐项转委托的结果。
 */
export interface CenterDelegateTaskResult {
  /**
   * 原任务 ID。
   */
  taskId: string
  /**
   * 此项是否成功。
   */
  succeeded: boolean
  /**
   * 失败错误码。
   */
  code?: string | null
  /**
   * 失败时的中文说明。
   */
  message?: string | null
}

/**
 * 校验响应中的规则，拒绝缺失字段。
 * @param value 未验证响应。
 * @returns 是否为完整委托规则。
 */
function isDelegation(value: unknown): value is CenterDelegation {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const item = value as Record<string, unknown>
  return typeof item.id === 'string' && item.id.length > 0
    && typeof item.principalPersonId === 'string' && typeof item.delegatePersonId === 'string'
    && typeof item.startsAtUtc === 'string' && typeof item.endsAtUtc === 'string'
    && Array.isArray(item.flowCodes) && item.flowCodes.every(code => typeof code === 'string')
}

/**
 * 读取本人委托设置。
 * @param signal 页面离开时的取消信号。
 * @returns 持久规则列表。
 */
export async function loadCenterDelegations(signal?: AbortSignal): Promise<CenterDelegation[]> {
  const result = await getJson<unknown>('/api/v1/center/delegations', {
    headers: { Authorization: `Bearer ${await centerAccessToken()}` }, signal, cache: 'no-store',
  })
  if (!Array.isArray(result) || !result.every(isDelegation)) throw new Error('委托设置响应不完整，请稍后重试。')
  return result
}

/**
 * 持久保存本人委托规则。
 * @param input 已转换成 UTC 的规则。
 * @returns 服务端确认后的规则。
 */
export async function createCenterDelegation(input: CenterDelegationInput): Promise<CenterDelegation> {
  const result = await requestJson<unknown>('/api/v1/center/delegations', {
    method: 'POST', headers: { Authorization: `Bearer ${await centerAccessToken()}` }, body: input, cache: 'no-store',
  })
  if (!isDelegation(result)) throw new Error('委托设置保存响应不完整，请重新读取。')
  return result
}

/**
 * 删除本人规则，已有任务保持原委托关系。
 * @param id 规则 ID。
 * @returns 服务端确认删除。
 */
export async function deleteCenterDelegation(id: string): Promise<void> {
  await requestJson<void>(`/api/v1/center/delegations/${encodeURIComponent(id)}`, {
    method: 'DELETE', headers: { Authorization: `Bearer ${await centerAccessToken()}` }, cache: 'no-store',
  })
}

/**
 * 将已选在手任务逐项转给当前有效的代理人。
 * @param taskIds 服务端详情返回的任务 ID。
 * @returns 每项独立的成功或错误。
 */
export async function delegateCenterTasks(taskIds: string[]): Promise<CenterDelegateTaskResult[]> {
  const result = await requestJson<unknown>('/api/v1/center/delegations/tasks', {
    method: 'POST', headers: { Authorization: `Bearer ${await centerAccessToken()}`, 'X-Request-Id': crypto.randomUUID() },
    body: { taskIds }, cache: 'no-store',
  })
  if (!Array.isArray(result) || result.length !== taskIds.length || !result.every(item => {
    if (!item || typeof item !== 'object') return false
    const entry = item as Record<string, unknown>
    return typeof entry.taskId === 'string' && typeof entry.succeeded === 'boolean'
  })) throw new Error('已有任务委托响应不完整，请刷新后核对。')
  return result as CenterDelegateTaskResult[]
}
