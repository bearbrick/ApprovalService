import { centerAccessToken } from './center-session.ts'
import { adminAccessToken } from './admin-session.ts'

/**
 * 执行打印相关请求，凭据只放请求头。
 * @param path 接口路径。
 * @param admin 是否使用管理会话。
 * @param body 可选上传文件。
 * @returns 已检查成功状态的响应。
 */
export async function printingRequest(path: string, admin: boolean, body?: File): Promise<Response> {
  const token = admin ? await adminAccessToken() : await centerAccessToken()
  const response = await fetch(path, { method: body ? 'POST' : 'GET', body,
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store', signal: AbortSignal.timeout(120_000) })
  if (!response.ok) {
    let message = `打印服务请求失败（HTTP ${response.status}）。`
    try {
      const error: unknown = await response.json()
      if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') message = error.message
    } catch {
      // 非 JSON 错误保留 HTTP 状态提示。
    }
    throw new Error(message)
  }
  return response
}
