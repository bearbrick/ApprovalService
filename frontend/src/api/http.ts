/**
 * 调后端接口的公共部分：统一的错误格式转成带错误码的异常。
 */

/**
 * 服务端出错时的响应体（和后端 Contracts 的 ApiErrorResponse 一致，见 docs/contracts.md）。
 */
export interface ApiErrorBody {
  /**
   * 错误码，如 NOT_FOUND。
   */
  code: string
  /**
   * 给人看的中文说明。
   */
  message: string
  /**
   * 链路 ID，反馈问题时提供。
   */
  traceId?: string
}

/**
 * 服务信息（GET /api/v1/info）。
 */
export interface ServiceInfo {
  /**
   * 服务名称。
   */
  name: string
  /**
   * 服务程序的版本。
   */
  version: string
  /**
   * 开放接口的版本。
   */
  apiVersion: string
}

/**
 * 接口调用失败：带服务端的错误码和链路 ID。
 */
export class ApiError extends Error {
  /**
   * 错误码；连不上服务时为 NETWORK_ERROR。
   */
  readonly code: string
  /**
   * 链路 ID；没有时为空字符串。
   */
  readonly traceId: string

  /**
   * 创建异常。
   * @param code 错误码。
   * @param message 中文说明。
   * @param traceId 链路 ID。
   */
  constructor(code: string, message: string, traceId = '') {
    super(message)
    this.code = code
    this.traceId = traceId
  }
}

/**
 * 发一个 GET 请求，返回解析好的 JSON。
 * @param path 接口路径，如 /api/v1/info。
 * @returns 响应体。
 */
export async function getJson<T>(path: string): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, { headers: { Accept: 'application/json' } })
  } catch (error) {
    throw new ApiError('NETWORK_ERROR', '连不上审批流服务：' + String(error))
  }
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as ApiErrorBody | null
    throw new ApiError(body?.code ?? 'HTTP_' + response.status, body?.message ?? `服务返回 ${response.status}`, body?.traceId ?? '')
  }
  return (await response.json()) as T
}

/**
 * 取服务信息（顶栏显示连接状态用）。
 * @returns 服务信息。
 */
export const getServiceInfo = () => getJson<ServiceInfo>('/api/v1/info')
