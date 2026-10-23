import { requestJson } from './http.ts'
import { centerAccessToken } from './center-session.ts'

/**
 * 服务端从当前嵌入 JWT 返回的接入应用与允许来源。
 */
export interface CenterEmbedContext {
  /**
   * 签发嵌入会话的接入应用编码。
   */
  clientAppCode: string
  /**
   * 该应用登记的完整网页来源。
   */
  allowedOrigins: string[]
}

/**
 * 只包含审批变化提示的跨窗口消息。
 */
export interface CenterChangeMessage {
  /**
   * 已改变的审批单 ID。
   */
  instanceId: string
  /**
   * 已处理的任务 ID。
   */
  taskId: string
  /**
   * 提醒外层重新读取状态，不传递业务数据。
   */
  status: 'changed'
}

/**
 * 可接收跨窗口通知的父窗口最小接口。
 */
export interface CenterChangeSink {
  /**
   * 向明确来源发送最小变化通知。
   * @param message 不含令牌和业务数据的通知。
   * @param targetOrigin 已登记的目标来源。
   */
  postMessage(message: CenterChangeMessage, targetOrigin: string): void
}

/**
 * 从路由片段删除一次性令牌，删除完成后才返回令牌供网络交换。
 * @param hash 当前路由片段。
 * @param replace 用不产生新历史条目的方式写回已清理片段。
 * @returns 令牌、清理后的路由；无令牌时为空。
 */
export function consumeEmbedToken(hash: string, replace: (cleanHash: string) => void): { token: string, cleanHash: string } | null {
  const queryIndex = hash.indexOf('?')
  if (queryIndex < 0) return null
  const route = hash.slice(0, queryIndex)
  const parameters = new URLSearchParams(hash.slice(queryIndex + 1))
  if (!parameters.has('embedToken')) return null
  const tokens = parameters.getAll('embedToken')
  parameters.delete('embedToken')
  const rest = parameters.toString()
  const cleanHash = route + (rest ? `?${rest}` : '')
  // 即使令牌重复或无效，也先清除地址和当前历史条目，避免再次暴露。
  replace(cleanHash)
  if (tokens.length !== 1 || !tokens[0] || tokens[0].length > 2048 || /\s/.test(tokens[0])) {
    throw new Error('内嵌登录令牌格式不正确，请从业务系统重新打开。')
  }
  return { token: tokens[0], cleanHash }
}

/**
 * 在页面加载初期从地址与历史中清除一次性令牌。
 * @returns 令牌、清理后的路由；无令牌时为空。
 */
export function takeEmbedTokenFromHash(): { token: string, cleanHash: string } | null {
  return consumeEmbedToken(window.location.hash, cleanHash => {
    window.history.replaceState(window.history.state, '',
      `${window.location.pathname}${window.location.search}${cleanHash}`)
  })
}

/**
 * 使用已兑换的嵌入 JWT 取得服务端登记来源。
 * @returns 接入应用和可信来源列表。
 */
export async function loadCenterEmbedContext(): Promise<CenterEmbedContext> {
  const result = await requestJson<CenterEmbedContext>('/api/v1/center/embed-context', {
    method: 'GET', headers: { Authorization: `Bearer ${await centerAccessToken()}` }, cache: 'no-store',
  })
  if (!result || typeof result.clientAppCode !== 'string' || !result.clientAppCode ||
    !Array.isArray(result.allowedOrigins) || result.allowedOrigins.some(origin => typeof origin !== 'string')) {
    throw new Error('内嵌来源响应不完整，请从业务系统重新打开。')
  }
  return result
}

/**
 * 仅在父页面的来源与当前嵌入应用登记来源完全相符时返回目标来源。
 * @param context 服务端对当前嵌入 JWT 的来源响应。
 * @param referrer 当前页面的浏览器来源引用。
 * @returns 精确目标来源；不匹配时为空。
 */
export function registeredParentOrigin(context: CenterEmbedContext, referrer: string): string | null {
  if (!referrer) return null
  try {
    const origin = new URL(referrer).origin
    return context.allowedOrigins.includes(origin) ? origin : null
  } catch {
    return null
  }
}

/**
 * 限定兑换所得会话只打开签发时指定的片段路由。
 * @param targetPath 服务端在嵌入令牌中绑定的目标页面。
 * @param cleanHash 清除一次性令牌后的当前片段路由。
 * @returns 两个路由完全一致时为真。
 */
export function matchesEmbedTarget(targetPath: string | null | undefined, cleanHash: string): boolean {
  return typeof targetPath === 'string' && targetPath.startsWith('#/') && targetPath === cleanHash
}

/**
 * 审批写入成功后仅向登记来源发送审批单和任务 ID。
 * @param context 当前嵌入应用的可信来源响应。
 * @param referrer 浏览器提供的父页面来源引用。
 * @param parent 跨窗口消息接收窗口；普通独立页面传空。
 * @param instanceId 已更新的审批单 ID。
 * @param taskId 已处理的任务 ID。
 * @returns 是否发送了通知。
 */
export function postCenterChange(context: CenterEmbedContext, referrer: string, parent: CenterChangeSink | null,
  instanceId: string, taskId: string): boolean {
  const origin = registeredParentOrigin(context, referrer)
  if (!origin || !parent || !instanceId || !taskId) return false
  parent.postMessage({ instanceId, taskId, status: 'changed' }, origin)
  return true
}
