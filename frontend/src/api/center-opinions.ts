import { getJson, requestJson } from './http.ts'
import { centerAccessToken } from './center-session.ts'

/**
 * 当前人员保存的一条常用审批意见。
 */
export interface CenterOpinion {
  /**
   * 意见标识。
   */
  id: string
  /**
   * 意见正文。
   */
  content: string
  /**
   * UTC 创建时间。
   */
  createdAtUtc: string
}

/**
 * 校验单条意见响应，不信任未验证的服务端 JSON。
 * @param value 待校验响应。
 * @returns 是否为完整意见。
 */
function isOpinion(value: unknown): value is CenterOpinion {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const item = value as Record<string, unknown>
  return typeof item.id === 'string' && item.id.length > 0 && typeof item.content === 'string'
    && item.content.length > 0 && item.content.length <= 500 && typeof item.createdAtUtc === 'string'
}

/**
 * 读取当前登录人员的常用意见，嵌入会话也可以读取。
 * @param signal 页面切换时的取消信号。
 * @returns 已保存的意见。
 */
export async function loadCenterOpinions(signal?: AbortSignal): Promise<CenterOpinion[]> {
  const result = await getJson<unknown>('/api/v1/center/opinions', {
    headers: { Authorization: `Bearer ${await centerAccessToken()}` }, signal, cache: 'no-store',
  })
  if (!Array.isArray(result) || result.length > 30 || !result.every(isOpinion)) {
    throw new Error('常用意见响应不完整，请稍后重试。')
  }
  return result
}

/**
 * 新增本人常用意见。
 * @param content 意见正文。
 * @returns 服务端确认的意见。
 */
export async function createCenterOpinion(content: string): Promise<CenterOpinion> {
  const result = await requestJson<unknown>('/api/v1/center/opinions', {
    method: 'POST', headers: { Authorization: `Bearer ${await centerAccessToken()}` }, body: { content }, cache: 'no-store',
  })
  if (!isOpinion(result)) throw new Error('常用意见保存响应不完整，请重新读取。')
  return result
}

/**
 * 修改本人拥有的常用意见。
 * @param id 意见标识。
 * @param content 新正文。
 * @returns 服务端确认的意见。
 */
export async function updateCenterOpinion(id: string, content: string): Promise<CenterOpinion> {
  const result = await requestJson<unknown>(`/api/v1/center/opinions/${encodeURIComponent(id)}`, {
    method: 'PUT', headers: { Authorization: `Bearer ${await centerAccessToken()}` }, body: { content }, cache: 'no-store',
  })
  if (!isOpinion(result) || result.id !== id) throw new Error('常用意见修改响应不完整，请重新读取。')
  return result
}

/**
 * 删除本人拥有的常用意见。
 * @param id 意见标识。
 * @returns 服务端确认删除。
 */
export async function deleteCenterOpinion(id: string): Promise<void> {
  await requestJson<void>(`/api/v1/center/opinions/${encodeURIComponent(id)}`, {
    method: 'DELETE', headers: { Authorization: `Bearer ${await centerAccessToken()}` }, cache: 'no-store',
  })
}
