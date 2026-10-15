import { onBeforeUnmount, onMounted, readonly, ref } from 'vue'
import type { DeepReadonly, Ref } from 'vue'
import { ApiError, getServiceInfo } from '../api/http.ts'
import type { ServiceInfo } from '../api/http.ts'

/**
 * 服务连接的显示状态。
 */
export type ServiceConnectionState = 'loading' | 'connected' | 'error'

/**
 * 服务连接状态与手动重试入口。
 */
export interface ServiceConnection {
  /**
   * 当前连接状态。
   */
  state: Readonly<Ref<ServiceConnectionState>>
  /**
   * 最近一次成功读取的信息；请求中或失败时为空。
   */
  info: DeepReadonly<Ref<ServiceInfo | null>>
  /**
   * 可展示的当前错误；主动取消旧请求不覆盖新状态。
   */
  error: DeepReadonly<Ref<ApiError | null>>
  /**
   * 取消前次请求并重新获取服务信息，卸载后不再发起请求。
   * @returns 当前刷新结束的通知，失败通过 error 暴露。
   */
  refresh(): Promise<void>
}

/**
 * 管理组件生命周期内的服务连接，保证过期响应不能覆盖新的请求。
 * @returns 只读响应式状态、服务信息、错误与刷新方法。
 */
export function useServiceConnection(): ServiceConnection {
  const state = ref<ServiceConnectionState>('loading')
  const info = ref<ServiceInfo | null>(null)
  const error = ref<ApiError | null>(null)
  let current: AbortController | undefined
  let requestVersion = 0
  let disposed = false

  /**
   * 重新连接服务，版本号同时隔离成功和失败的过期响应。
   * @returns 本次请求结束的通知。
   */
  async function refresh(): Promise<void> {
    if (disposed) return
    const version = ++requestVersion
    current?.abort()
    const controller = new AbortController()
    current = controller
    state.value = 'loading'
    info.value = null
    error.value = null
    try {
      const result = await getServiceInfo({ signal: controller.signal })
      if (disposed || version !== requestVersion) return
      info.value = result
      state.value = 'connected'
    } catch (failure) {
      if (disposed || version !== requestVersion || controller.signal.aborted) return
      error.value = failure instanceof ApiError ? failure : new ApiError('NETWORK_ERROR', '无法读取服务连接状态，请重试。', '', { kind: 'network' })
      state.value = 'error'
    } finally {
      if (version === requestVersion) current = undefined
    }
  }

  onMounted(refresh)
  onBeforeUnmount(() => {
    disposed = true
    requestVersion++
    current?.abort()
    current = undefined
  })
  return { state: readonly(state), info: readonly(info), error: readonly(error), refresh }
}
