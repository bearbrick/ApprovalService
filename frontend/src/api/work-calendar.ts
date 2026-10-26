import { adminAccessToken } from './admin-session.ts'
import { getJson, requestJson } from './http.ts'
import { readWorkCalendar } from '../model/work-calendar.ts'
import type { WorkCalendar } from '../model/work-calendar.ts'

/**
 * 使用当前本地管理员 JWT 读取工作日历。
 * @param signal 页面卸载或刷新时的取消信号。
 * @returns 当前完整日历。
 */
export async function loadWorkCalendar(signal?: AbortSignal): Promise<WorkCalendar> {
  const token = await adminAccessToken()
  return readWorkCalendar(await getJson<unknown>('/api/v1/admin/work-calendar', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store', signal,
  }))
}

/**
 * 按编辑时的修订保存完整工作日历；时区不提交。
 * @param value 当前编辑的日历及旧修订。
 * @param reason 审计原因。
 * @returns 本次保存的新日历。
 */
export async function saveWorkCalendar(value: WorkCalendar, reason: string): Promise<WorkCalendar> {
  const token = await adminAccessToken()
  return readWorkCalendar(await requestJson<unknown>('/api/v1/admin/work-calendar', {
    method: 'PUT', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
    body: { expectedRevision: value.revision, workingStart: value.workingStart,
      workingEnd: value.workingEnd, overrides: value.overrides, reason },
  }))
}
