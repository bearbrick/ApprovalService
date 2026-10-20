import { createRenderer } from 'vue'
import { ApiError, getJson, getServiceInfo, requestJson } from '../src/api/http.ts'
import type { JsonRequestOptions } from '../src/api/http.ts'
import { useServiceConnection } from '../src/composables/useServiceConnection.ts'
import type { ServiceConnection } from '../src/composables/useServiceConnection.ts'

/**
 * 检查程序提供的断言回调。
 * @param name 检查名称。
 * @param value 条件是否成立。
 */
type Check = (name: string, value: boolean) => void

/**
 * 运行请求并捕获统一异常；成功或其他异常直接使检查失败。
 * @param options 注入请求和响应行为的选项。
 * @returns 本次捕获的接口异常。
 */
async function captureError(options: JsonRequestOptions): Promise<ApiError> {
  try {
    await requestJson('/api/v1/check', options)
  } catch (error) {
    if (error instanceof ApiError) return error
    throw error
  }
  throw new Error('检查预期请求失败，但请求成功。')
}

/**
 * 创建仅在信号取消后结束的请求，不依赖真实网络。
 * @param input 请求地址。
 * @param init 请求设置。
 * @returns 被取消时拒绝的响应承诺。
 */
function waitForAbort(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  void input
  return new Promise((resolve, reject) => {
    void resolve
    const abort = () => reject(new DOMException('检查取消', 'AbortError'))
    if (init?.signal?.aborted) abort()
    else init?.signal?.addEventListener('abort', abort, { once: true })
  })
}

/**
 * 检查统一 HTTP 请求，包括取消、超时、非 JSON 与链路信息。
 * @param check 主检查程序的断言回调。
 * @returns 所有异步检查完成的通知。
 */
export async function runHttpChecks(check: Check): Promise<void> {
  let sent: RequestInit | undefined
  let calls = 0
  const success = await requestJson<{ result: string }>('/api/v1/check', {
    method: 'POST', body: { amount: 12 }, headers: { 'X-Request-Id': 'caller-1' },
    fetcher: async (input, init) => {
      void input
      calls++
      sent = init
      return Response.json({ result: '完成' })
    },
  })
  check('通用 JSON 请求保留方法、序列化正文和调用方请求头', success.result === '完成'
    && sent?.method === 'POST' && sent.body === '{"amount":12}' && new Headers(sent.headers).get('X-Request-Id') === 'caller-1')
  check('JSON 请求默认声明响应与正文类型，且只发送一次', new Headers(sent?.headers).get('Accept') === 'application/json'
    && new Headers(sent?.headers).get('Content-Type') === 'application/json' && calls === 1)

  const serverError = await captureError({ fetcher: async () => Response.json({
    code: 'VALIDATION_FAILED', message: '数量必须大于零。', traceId: 'body-trace',
    errors: [{ field: 'data.qty', code: 'VALIDATION_FAILED', message: '数量必须大于零。' }, null, { field: 3 }],
  }, { status: 400, headers: { 'X-Trace-Id': 'header-trace', 'X-Request-Id': 'response-request' } }) })
  check('服务端异常保留 HTTP 状态、错误码、中文说明和正文链路编号', serverError.status === 400
    && serverError.code === 'VALIDATION_FAILED' && serverError.message === '数量必须大于零。' && serverError.traceId === 'body-trace')
  check('字段错误只保留有效条目，并保留响应请求编号', serverError.errors.length === 1
    && serverError.errors[0]?.field === 'data.qty' && serverError.requestId === 'response-request')

  const malformedError = await captureError({ fetcher: async () => Response.json({ code: 12, message: null, traceId: {} },
    { status: 409, headers: { 'X-Trace-Id': 'fallback-trace' } }) })
  check('异常正文属性类型错误时使用 HTTP 提示和响应头链路编号', malformedError.code === 'HTTP_409'
    && malformedError.status === 409 && malformedError.traceId === 'fallback-trace' && malformedError.errors.length === 0)

  let attempts = 0
  const gatewayError = await captureError({ method: 'POST', body: { action: 'check' }, fetcher: async () => {
    attempts++
    return new Response('<html>不可用</html>', { status: 503, headers: { 'X-Trace-Id': 'gateway-trace' } })
  } })
  check('网关非 JSON 错误保留状态和链路编号，不自动重试写请求', gatewayError.status === 503
    && gatewayError.code === 'HTTP_503' && gatewayError.traceId === 'gateway-trace' && attempts === 1)

  const invalidJson = await captureError({ fetcher: async () => new Response('not-json', {
    status: 200, headers: { 'X-Trace-Id': 'invalid-trace' },
  }) })
  check('成功响应中无效 JSON 归类为响应格式错误并保留状态', invalidJson.kind === 'invalid-response'
    && invalidJson.code === 'INVALID_RESPONSE' && invalidJson.status === 200 && invalidJson.traceId === 'invalid-trace')
  const empty = await requestJson<void>('/api/v1/check', { fetcher: async () => new Response(null, { status: 204 }) })
  check('204 无内容响应直接返回 undefined', empty === undefined)

  const network = await captureError({ fetcher: async () => { throw new TypeError('检查网络不可达') } })
  check('网络不可达返回中文网络异常，无响应状态为零', network.code === 'NETWORK_ERROR'
    && network.kind === 'network' && network.status === 0 && network.traceId === '')
  const timeout = await captureError({ timeoutMs: 5, fetcher: waitForAbort })
  check('请求超时与网络错误明确区分', timeout.code === 'TIMEOUT' && timeout.kind === 'timeout')

  const beforeStart = new AbortController()
  beforeStart.abort()
  let started = false
  const beforeError = await captureError({ signal: beforeStart.signal, fetcher: async () => {
    started = true
    return Response.json({})
  } })
  check('已取消的请求不会发送网络请求', !started && beforeError.kind === 'cancelled')
  const inflight = new AbortController()
  const waiting = captureError({ signal: inflight.signal, fetcher: waitForAbort })
  inflight.abort('调用方退出页面')
  check('正在进行的请求响应主动取消信号', (await waiting).kind === 'cancelled')

  const bodyTimeout = await captureError({ timeoutMs: 5, fetcher: async (input, init) => {
    void input
    const stream = new ReadableStream({ start(controller) {
      init?.signal?.addEventListener('abort', () => controller.error(new DOMException('检查取消', 'AbortError')), { once: true })
    } })
    return new Response(stream, { headers: { 'X-Trace-Id': 'body-timeout' } })
  } })
  check('超时包含响应体读取阶段，保留已收到的 HTTP 状态和链路编号', bodyTimeout.kind === 'timeout'
    && bodyTimeout.status === 200 && bodyTimeout.traceId === 'body-timeout')

  let method = ''
  await getJson('/api/v1/check', { method: 'PATCH', fetcher: async (input, init) => {
    void input
    method = init?.method ?? ''
    return Response.json({})
  } })
  check('GET 兼容入口固定请求方法', method === 'GET')
  let path = ''
  const info = await getServiceInfo({ fetcher: async (input) => {
    path = String(input)
    return Response.json({ name: 'ApprovalService', version: '0.1.0', apiVersion: 'v1' })
  } })
  check('服务信息入口沿用既有路径与响应模型', path === '/api/v1/info' && info.name === 'ApprovalService' && info.apiVersion === 'v1')
  for (const [name, body] of [
    ['空值', null], ['空对象', {}], ['数组', []],
    ['字段类型错误', { name: 'ApprovalService', version: 1, apiVersion: 'v1' }],
    ['空字符串', { name: '', version: '0.1.0', apiVersion: 'v1' }],
    ['空白字符串', { name: 'ApprovalService', version: '0.1.0', apiVersion: '  ' }],
  ] as const) {
    let invalid: unknown
    try {
      await getServiceInfo({ fetcher: async () => Response.json(body, { headers: { 'X-Trace-Id': 'shape-trace' } }) })
    } catch (error) {
      invalid = error
    }
    check(`服务信息拒绝${name}并保留状态和链路编号`, invalid instanceof ApiError
      && invalid.kind === 'invalid-response' && invalid.status === 200 && invalid.traceId === 'shape-trace')
  }
  await checkServiceConnection(check)
}

/**
 * 检查连接组合函数的生命周期，使用 Vue 自定义渲染器避免浏览器和网络依赖。
 * @param check 主检查程序的断言回调。
 * @returns 连接场景检查完成的通知。
 */
async function checkServiceConnection(check: Check): Promise<void> {
  const savedFetch = globalThis.fetch
  const pending: Array<{
    /**
     * 请求取消信号。
     */
    signal: AbortSignal | null | undefined
    /**
     * 允许检查按指定次序完成响应。
     */
    resolve: (response: Response) => void
  }> = []
  globalThis.fetch = (input, init) => {
    void input
    return new Promise((resolve) => pending.push({ signal: init?.signal, resolve }))
  }
  const renderer = createRenderer<object, object>({
    patchProp: () => {}, insert: () => {}, remove: () => {}, createElement: () => ({}),
    createText: () => ({}), createComment: () => ({}), setText: () => {}, setElementText: () => {},
    parentNode: () => null, nextSibling: () => null,
  })
  let connection: ServiceConnection | undefined
  const app = renderer.createApp({ setup() { connection = useServiceConnection(); return () => null } })
  try {
    app.mount({})
    const state = connection!
    check('连接状态在挂载时进入加载并只发起一次请求', state.state.value === 'loading' && pending.length === 1)
    const refreshing = state.refresh()
    check('重试服务连接会取消上一次请求', pending[0]?.signal?.aborted === true && pending.length === 2)
    pending[1]!.resolve(Response.json({ name: 'ApprovalService', version: 'new', apiVersion: 'v1' }))
    await refreshing
    check('新的请求成功后展示最新服务版本', state.state.value === 'connected' && state.info.value?.version === 'new' && state.error.value === null)
    pending[0]!.resolve(Response.json({ name: 'ApprovalService', version: 'old', apiVersion: 'v1' }))
    await new Promise((resolve) => setTimeout(resolve, 0))
    check('晚到的旧响应不能覆盖新连接状态', state.info.value?.version === 'new')

    const failed = state.refresh()
    pending[2]!.resolve(Response.json({ code: 'INTERNAL_ERROR', message: '服务暂时不可用。', traceId: 'connection-trace' }, { status: 500 }))
    await failed
    check('连接失败向界面提供统一异常和可重试状态', state.state.value === 'error'
      && state.error.value?.traceId === 'connection-trace' && state.info.value === null)
    const unmounting = state.refresh()
    app.unmount()
    check('卸载组件会取消在途连接请求', pending[3]?.signal?.aborted === true)
    pending[3]!.resolve(Response.json({ name: 'ApprovalService', version: 'after-unmount', apiVersion: 'v1' }))
    await unmounting
    const count = pending.length
    await state.refresh()
    check('卸载后的响应和刷新不会写入数据或重新请求', state.info.value === null && pending.length === count)
  } finally {
    app.unmount()
    globalThis.fetch = savedFetch
  }
}
