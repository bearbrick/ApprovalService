import { centerAccessToken, centerSession } from './center-session.ts'

/**
 * 使用当前人员 JWT 从服务端现有验权文件接口读取附件字节。
 * @param id 详情响应返回的附件 ID。
 * @returns 已授权文件内容，不把 JWT 放进 URL。
 */
export async function fetchCenterFile(id: string): Promise<Blob> {
  if (!centerSession()) throw new Error('请先登录审批中心。')
  if (!id) throw new Error('附件标识不正确。')
  const response = await fetch(`/api/v1/files/${encodeURIComponent(id)}`, {
    method: 'GET', headers: { Authorization: `Bearer ${await centerAccessToken()}` },
    cache: 'no-store', signal: AbortSignal.timeout(30_000),
  })
  if (!response.ok) {
    let message = `附件读取失败（HTTP ${response.status}）。`
    if (response.headers.get('Content-Type')?.includes('application/json')) {
      try {
        const body: unknown = await response.json()
        if (body && typeof body === 'object' && 'message' in body && typeof body.message === 'string') {
          message = body.message
        }
      } catch {
        // 网关可能返回损坏的 JSON；保留 HTTP 状态提示即可。
      }
    }
    throw new Error(message)
  }
  return await response.blob()
}
