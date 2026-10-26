import { getJson } from './http.ts'
import { centerAccessToken } from './center-session.ts'

/**
 * 授权分页响应仅需的计数字段。
 */
interface CenterCountResponse {
  /**
   * 当前人员的真实待办审批单数。
   */
  total: number
}

/**
 * 从人员 JWT 授权列表取待办角标，不从当前页面旧列表推断。
 * @param signal 路由或会话切换时的取消信号。
 * @returns 当前人员的待办审批单数。
 */
export async function loadCenterTodoCount(signal: AbortSignal): Promise<number> {
  const result = await getJson<CenterCountResponse>('/api/v1/center/instances?section=todo&skip=0&take=1', {
    headers: { Authorization: `Bearer ${await centerAccessToken()}` }, signal, cache: 'no-store',
  })
  if (!result || !Number.isSafeInteger(result.total) || result.total < 0) {
    throw new Error('待办数量响应不完整，请刷新后重试。')
  }
  return result.total
}
