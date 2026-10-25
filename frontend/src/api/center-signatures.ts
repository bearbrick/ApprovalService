import { requestJson } from './http.ts'
import { centerAccessToken, centerSession } from './center-session.ts'

/**
 * 服务端返回的不可变本人签名元数据。
 */
export interface CenterSignature {
  /**
   * 签名资产 ID。
   */
  id: string
  /**
   * 原始 PNG 的 SHA-256 摘要。
   */
  sha256: string
  /**
   * 是否可在个人设置中再次选择。
   */
  isPersonal: boolean
  /**
   * 上传时间。
   */
  uploadedAt: string
}

/**
 * 从错误响应中读取安全的中文提示。
 * @param response 服务端响应。
 * @returns 错误对象。
 */
async function responseError(response: Response): Promise<Error> {
  try {
    const body: unknown = await response.json()
    if (body && typeof body === 'object' && 'message' in body && typeof body.message === 'string') {
      return new Error(body.message)
    }
  } catch {
    // 网关返回非 JSON 时仅展示 HTTP 状态。
  }
  return new Error(`签名操作失败（HTTP ${response.status}）。`)
}

/**
 * 校验签名元数据的关键字段。
 * @param value 未信任响应。
 * @returns 是否为完整资产。
 */
function isSignature(value: unknown): value is CenterSignature {
  return Boolean(value && typeof value === 'object' &&
    'id' in value && typeof value.id === 'string' && value.id &&
    'sha256' in value && typeof value.sha256 === 'string')
}

/**
 * 上传本次手写 PNG；个人资产只允许本地会话创建。
 * @param png 画布生成的 PNG。
 * @param personal 是否列为可再次选择的个人签名。
 * @returns 不可变资产元数据。
 */
export async function uploadCenterSignature(png: Blob, personal: boolean): Promise<CenterSignature> {
  if (!centerSession()) throw new Error('请先登录审批中心。')
  if (png.type !== 'image/png' || png.size < 1 || png.size > 2 * 1024 * 1024) {
    throw new Error('手写签名必须是小于 2 MiB 的 PNG。')
  }
  const response = await fetch(`/api/v1/center/signatures?personal=${personal}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${await centerAccessToken()}`, 'Content-Type': 'image/png' },
    body: png, cache: 'no-store', signal: AbortSignal.timeout(30_000),
  })
  if (!response.ok) throw await responseError(response)
  const result: unknown = await response.json()
  if (!isSignature(result)) throw new Error('签名上传响应不完整，请重试。')
  return result
}

/**
 * 读取本人最近上传的个人签名，绝不隐式用于审批操作。
 * @returns 资产元数据或空。
 */
export async function latestCenterSignature(): Promise<CenterSignature | null> {
  if (!centerSession()) throw new Error('请先登录审批中心。')
  const result = await requestJson<CenterSignature | null>('/api/v1/center/signatures/me', {
    headers: { Authorization: `Bearer ${await centerAccessToken()}` }, cache: 'no-store',
  })
  if (result !== null && !isSignature(result)) throw new Error('个人签名响应不完整，请重试。')
  return result
}

/**
 * 按本人或已授权审批单下载签名 PNG，JWT 不进入 URL。
 * @param id 签名资产 ID。
 * @param instanceId 历史审批单 ID；个人预览时省略。
 * @returns 原始 PNG。
 */
export async function fetchCenterSignature(id: string, instanceId?: string): Promise<Blob> {
  if (!centerSession()) throw new Error('请先登录审批中心。')
  const query = instanceId ? `?instanceId=${encodeURIComponent(instanceId)}` : ''
  const response = await fetch(`/api/v1/center/signatures/${encodeURIComponent(id)}${query}`, {
    headers: { Authorization: `Bearer ${await centerAccessToken()}` }, cache: 'no-store',
    signal: AbortSignal.timeout(30_000),
  })
  if (!response.ok) throw await responseError(response)
  const blob = await response.blob()
  if (blob.type !== 'image/png') throw new Error('签名图片类型不正确。')
  return blob
}
