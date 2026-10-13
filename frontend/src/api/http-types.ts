/**
 * 接口返回的逐字段错误，与服务端统一错误体保持一致。
 */
export interface ApiFieldError {
  /**
   * 字段路径，例如 data.items[0].qty。
   */
  field: string
  /**
   * 字段错误码。
   */
  code: string
  /**
   * 给使用者看的中文错误说明。
   */
  message: string
}

/**
 * 服务端统一错误响应，权威定义见后端 Contracts。
 */
export interface ApiErrorBody {
  /**
   * 服务端错误码。
   */
  code: string
  /**
   * 中文错误说明。
   */
  message: string
  /**
   * 请求链路编号。
   */
  traceId?: string
  /**
   * 可定位到字段的错误列表。
   */
  errors?: ApiFieldError[]
}

/**
 * 前端错误分类；取消和无效响应属于本地状态，不是新增服务端契约。
 */
export type ApiErrorKind = 'http' | 'network' | 'timeout' | 'cancelled' | 'invalid-response'

/**
 * 异常的请求上下文。
 */
export interface ApiErrorContext {
  /**
   * HTTP 状态码；未收到响应时为零。
   */
  status?: number
  /**
   * 字段错误列表。
   */
  errors?: readonly ApiFieldError[]
  /**
   * 响应返回的请求编号。
   */
  requestId?: string
  /**
   * 错误来源分类。
   */
  kind?: ApiErrorKind
}

/**
 * 接口调用异常，保留中文说明、HTTP 状态、字段错误和链路信息。
 */
export class ApiError extends Error {
  /**
   * 服务端错误码或前端本地错误标记。
   */
  readonly code: string
  /**
   * 链路编号；服务未提供时为空字符串。
   */
  readonly traceId: string
  /**
   * HTTP 状态；无响应时为零。
   */
  readonly status: number
  /**
   * 字段错误；没有逐字段信息时为空数组。
   */
  readonly errors: readonly ApiFieldError[]
  /**
   * 请求编号；服务未提供时为空字符串。
   */
  readonly requestId: string
  /**
   * 错误来源，供界面区分主动取消与失败。
   */
  readonly kind: ApiErrorKind

  /**
   * 创建接口异常，保留原有前三个参数的调用方式。
   * @param code 错误码或本地错误标记。
   * @param message 中文说明。
   * @param traceId 链路编号。
   * @param context 附加的请求上下文。
   */
  constructor(code: string, message: string, traceId = '', context: ApiErrorContext = {}) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.traceId = traceId
    this.status = context.status ?? 0
    this.errors = context.errors ?? []
    this.requestId = context.requestId ?? ''
    this.kind = context.kind ?? 'http'
  }
}

/**
 * JSON 请求选项；请求体自动序列化，不提供自动重试。
 */
export interface JsonRequestOptions extends Omit<RequestInit, 'body' | 'signal'> {
  /**
   * 要序列化为 JSON 的请求体；省略时不发送请求体。
   */
  body?: unknown
  /**
   * 调用方的取消信号，不会由请求内部主动终止。
   */
  signal?: AbortSignal
  /**
   * 包含读取响应体在内的超时毫秒数，默认十五秒，零表示不设超时。
   */
  timeoutMs?: number
  /**
   * 可注入的请求函数；默认使用浏览器 fetch，检查程序可替换为本地实现。
   */
  fetcher?: typeof fetch
}

/**
 * 服务信息接口返回的基础信息。
 */
export interface ServiceInfo {
  /**
   * 服务名称。
   */
  name: string
  /**
   * 服务版本。
   */
  version: string
  /**
   * 开放接口版本。
   */
  apiVersion: string
}
