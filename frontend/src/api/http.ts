/**
 * 统一 JSON 请求入口，所有方法只发送一次，由调用方决定是否重试。
 */
import { ApiError } from './http-types.ts'
import type { ApiErrorContext, ApiFieldError, JsonRequestOptions, ServiceInfo } from './http-types.ts'

export { ApiError } from './http-types.ts'
export type { ApiErrorBody, ApiErrorKind, ApiFieldError, JsonRequestOptions, ServiceInfo } from './http-types.ts'

/**
 * 判断未知值是否为可读取属性的普通对象。
 * @param value 待检查的值。
 * @returns 是否为非空、非数组对象。
 */
function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * 只保留完整且类型正确的字段错误，避免异常响应再次导致界面异常。
 * @param value 服务端字段错误候选值。
 * @returns 可供界面展示的字段错误。
 */
function readFieldErrors(value: unknown): ApiFieldError[] {
  if (!Array.isArray(value)) return []
  return value.filter((item): item is ApiFieldError => isObject(item)
    && typeof item.field === 'string' && typeof item.code === 'string' && typeof item.message === 'string')
}

/**
 * 从响应构造上下文，读取 JSON 失败时也不会丢失响应头。
 * @param response 服务响应。
 * @returns 状态与请求编号。
 */
function responseContext(response: Response): ApiErrorContext {
  return { status: response.status, requestId: response.headers.get('X-Request-Id') ?? '' }
}

/**
 * 读取响应并转换为统一异常；成功的无内容响应返回 undefined。
 * @param response 服务响应。
 * @returns 解析后的 JSON，或无内容标记。
 */
async function readResponse(response: Response): Promise<unknown> {
  const traceId = response.headers.get('X-Trace-Id') ?? ''
  const context = responseContext(response)
  if (response.status === 204) return undefined
  const text = await response.text()
  let body: unknown
  try {
    body = JSON.parse(text)
  } catch {
    // HTTP 错误优先保留其状态；网关返回 HTML 时不把整页错误暴露给使用者。
    if (!response.ok) throw new ApiError('HTTP_' + response.status, `服务返回错误（HTTP ${response.status}）。`, traceId, context)
    throw new ApiError('INVALID_RESPONSE', '服务返回的内容不是有效的 JSON。', traceId, { ...context, kind: 'invalid-response' })
  }
  if (!response.ok) {
    const error = isObject(body) ? body : {}
    const code = typeof error.code === 'string' && error.code ? error.code : 'HTTP_' + response.status
    const message = typeof error.message === 'string' && error.message ? error.message : `服务返回错误（HTTP ${response.status}）。`
    const bodyTraceId = typeof error.traceId === 'string' && error.traceId ? error.traceId : traceId
    throw new ApiError(code, message, bodyTraceId, { ...context, errors: readFieldErrors(error.errors) })
  }
  return body
}

/**
 * 执行一次 JSON 请求，可对成功响应应用接口专属的结构校验。
 * @template T 调用方预期的响应体类型；204 接口应使用 void。
 * @param path 接口路径。
 * @param options 方法、请求体、超时、取消和请求函数等选项。
 * @param validate 可选的响应结构校验；失败时保留响应状态与链路信息。
 * @returns 解析好的响应体；204 返回 undefined。
 */
async function sendJson<T>(path: string, options: JsonRequestOptions, validate?: (value: unknown) => boolean): Promise<T> {
  const { body, signal, timeoutMs = 15000, fetcher = globalThis.fetch, ...init } = options
  if (!Number.isFinite(timeoutMs) || timeoutMs < 0) throw new RangeError('请求超时必须是大于或等于零的有限毫秒数。')
  const headers = new Headers(init.headers)
  if (!headers.has('Accept')) headers.set('Accept', 'application/json')
  if (body !== undefined && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  // 序列化失败属于调用代码缺陷，保留原始异常，不伪装成网络故障。
  const requestBody = body === undefined ? undefined : JSON.stringify(body)
  const controller = new AbortController()
  let timedOut = false
  let response: Response | undefined
  const cancel = () => controller.abort()
  if (signal?.aborted) controller.abort()
  else signal?.addEventListener('abort', cancel, { once: true })
  const timer = timeoutMs > 0 ? setTimeout(() => {
    // 主动取消先发生时保留取消原因，即使请求实现延后才结束也不改判为超时。
    if (controller.signal.aborted) return
    timedOut = true
    controller.abort()
  }, timeoutMs) : undefined
  try {
    controller.signal.throwIfAborted()
    response = await fetcher(path, { ...init, headers, body: requestBody, signal: controller.signal })
    controller.signal.throwIfAborted()
    const result = await readResponse(response)
    controller.signal.throwIfAborted()
    if (validate && !validate(result)) {
      throw new ApiError('INVALID_RESPONSE', '服务返回的数据结构不符合预期。', response.headers.get('X-Trace-Id') ?? '',
        { ...responseContext(response), kind: 'invalid-response' })
    }
    return result as T
  } catch (error) {
    const context = response ? responseContext(response) : {}
    const traceId = response?.headers.get('X-Trace-Id') ?? ''
    if (controller.signal.aborted) {
      if (timedOut) throw new ApiError('TIMEOUT', '请求审批流服务超时，请稍后重试。', traceId, { ...context, kind: 'timeout' })
      throw new ApiError('CANCELLED', '请求已取消。', traceId, { ...context, kind: 'cancelled' })
    }
    if (error instanceof ApiError) throw error
    throw new ApiError('NETWORK_ERROR', '无法连接审批流服务，请检查网络后重试。', traceId, { ...context, kind: 'network' })
  } finally {
    if (timer !== undefined) clearTimeout(timer)
    signal?.removeEventListener('abort', cancel)
  }
}

/**
 * 发送一次 JSON 请求，主动取消与超时分别归类，不自动重试。
 * @template T 调用方预期的响应体类型；204 接口应使用 void。
 * @param path 接口路径。
 * @param options 方法、请求体、超时和取消等设置。
 * @returns 解析好的响应体；204 返回 undefined。
 */
export function requestJson<T>(path: string, options: JsonRequestOptions = {}): Promise<T> {
  return sendJson<T>(path, options)
}

/**
 * 发送 GET 请求，复用统一的异常、超时与取消处理。
 * @template T 响应体类型。
 * @param path 接口路径。
 * @param options 可选请求设置，方法固定为 GET。
 * @returns 响应体。
 */
export function getJson<T>(path: string, options: JsonRequestOptions = {}): Promise<T> {
  return requestJson<T>(path, { ...options, method: 'GET' })
}

/**
 * 获取用于连接状态展示的服务信息，不涉及登录或鉴权业务。
 * @param options 请求超时与取消等设置。
 * @returns 服务名称、程序版本与接口版本。
 */
export function getServiceInfo(options: JsonRequestOptions = {}): Promise<ServiceInfo> {
  return sendJson<ServiceInfo>('/api/v1/info', { ...options, method: 'GET' }, isServiceInfo)
}

/**
 * 验证服务信息必要字段，避免成功状态携带空对象或其他无效内容。
 * @param value 响应中的未知数据。
 * @returns 是否包含非空的服务名称、程序版本和接口版本。
 */
function isServiceInfo(value: unknown): value is ServiceInfo {
  return isObject(value) && ['name', 'version', 'apiVersion'].every((key) => {
    const field = value[key]
    return typeof field === 'string' && field.trim().length > 0
  })
}
