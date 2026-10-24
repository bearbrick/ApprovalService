import { getJson, requestJson } from './http.ts'
import { centerAccessToken } from './center-session.ts'

/**
 * 已稳定的提醒类别及面向审批人的名称。
 */
export const notificationCategories = [
  { code: 'newTask', label: '新待办' },
  { code: 'urge', label: '被催办' },
  { code: 'beforeDue', label: '快到期' },
  { code: 'overdue', label: '已超时' },
  { code: 'result', label: '审批结果' },
  { code: 'cc', label: '被抄送' },
  { code: 'mention', label: '被提及' },
  { code: 'dailyDigest', label: '每日待办汇总' },
] as const

/**
 * 当前人员一类提醒的持久渠道开关。
 */
export interface CenterNotificationPreference {
  /**
   * 服务端提醒类别编码。
   */
  category: string
  /**
   * 是否接收邮件。
   */
  emailEnabled: boolean
  /**
   * 是否接收站内消息。
   */
  inAppEnabled: boolean
}

/**
 * 检查服务端返回的单项提醒设置。
 * @param value 待校验响应。
 * @returns 是否包含完整类别及两种渠道开关。
 */
function isPreference(value: unknown): value is CenterNotificationPreference {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const item = value as Record<string, unknown>
  return typeof item.category === 'string' && notificationCategories.some(category => category.code === item.category)
    && typeof item.emailEnabled === 'boolean' && typeof item.inAppEnabled === 'boolean'
    && (item.category !== 'dailyDigest' || item.inAppEnabled === false)
}

/**
 * 以当前页面人员 JWT 读取本人所有已保存或默认的提醒开关。
 * @param signal 页面切换时取消读取。
 * @returns 与服务端八类配置一一对应的设置。
 */
export async function loadCenterPreferences(signal?: AbortSignal): Promise<CenterNotificationPreference[]> {
  const result = await getJson<unknown>('/api/v1/notifications/preferences', {
    headers: { Authorization: `Bearer ${await centerAccessToken()}` }, signal, cache: 'no-store',
  })
  if (!Array.isArray(result) || result.length !== notificationCategories.length || !result.every(isPreference) ||
    new Set(result.map(item => item.category)).size !== notificationCategories.length) {
    throw new Error('提醒设置响应不完整，请稍后重试。')
  }
  const byCategory = new Map(result.map(item => [item.category, item]))
  return notificationCategories.map(category => byCategory.get(category.code)!)
}

/**
 * 修改当前人员一类提醒的渠道，只有服务端成功响应才视为已保存。
 * @param preference 当前人员该类别的已读取配置。
 * @param signal 页面离开时取消请求。
 * @returns 服务端确认后的持久配置。
 */
export async function saveCenterPreference(preference: CenterNotificationPreference,
  signal?: AbortSignal): Promise<CenterNotificationPreference> {
  if (!isPreference(preference)) throw new Error('提醒类别或渠道设置不正确。')
  const result = await requestJson<unknown>(`/api/v1/notifications/preferences/${encodeURIComponent(preference.category)}`, {
    method: 'PUT', headers: { Authorization: `Bearer ${await centerAccessToken()}` },
    body: { emailEnabled: preference.emailEnabled, inAppEnabled: preference.inAppEnabled },
    signal, cache: 'no-store',
  })
  if (!isPreference(result) || result.category !== preference.category) throw new Error('提醒设置保存响应不完整，请重新读取。')
  return result
}
