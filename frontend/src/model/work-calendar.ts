/**
 * 工作日历逐日覆盖。
 */
export interface WorkCalendarOverride {
  /**
   * yyyy-MM-dd 本地日期。
   */
  date: string
  /**
   * 是否为工作日。
   */
  isWorkingDay: boolean
}

/**
 * 受保护接口返回的当前日历。
 */
export interface WorkCalendar {
  /**
   * 乐观并发修订。
   */
  revision: number
  /**
   * 只读部署时区。
   */
  timeZoneId: string
  /**
   * HH:mm 开工时刻。
   */
  workingStart: string
  /**
   * HH:mm 结束时刻。
   */
  workingEnd: string
  /**
   * 完整的逐日覆盖。
   */
  overrides: WorkCalendarOverride[]
}

/**
 * 检查真实本地日期，避免浏览器自动把非法日期顺延。
 * @param value 日期文本。
 * @returns 是否为严格 yyyy-MM-dd 日期。
 */
export function validCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return false
  const date = new Date(`${value}T00:00:00Z`)
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value
}

/**
 * 返回工作时段或日期清单的首项错误。
 * @param start 每日开始。
 * @param end 每日结束。
 * @param overrides 日期清单。
 * @returns 中文错误；有效时为空。
 */
export function calendarValidation(start: string, end: string, overrides: WorkCalendarOverride[]): string {
  const time = /^(?:[01]\d|2[0-3]):[0-5]\d$/
  if (!time.test(start) || !time.test(end) || end <= start) return '工作时段结束须晚于开始，不支持跨夜。'
  if (overrides.some(item => !item || !validCalendarDate(item.date) || typeof item.isWorkingDay !== 'boolean')) {
    return '请为每行选择有效日期和日期类型。'
  }
  if (new Set(overrides.map(item => item.date)).size !== overrides.length) return '同一日期只能设置一次，请删除重复行。'
  return ''
}

/**
 * 验证响应结构及日期，拒绝把异常响应展示成可保存表单。
 * @param value 未知 HTTP 响应。
 * @returns 安全的日历响应。
 */
export function readWorkCalendar(value: unknown): WorkCalendar {
  const item = value as WorkCalendar | null
  if (!item || !Number.isSafeInteger(item.revision) || item.revision < 0
    || typeof item.timeZoneId !== 'string' || !item.timeZoneId
    || typeof item.workingStart !== 'string' || typeof item.workingEnd !== 'string'
    || !Array.isArray(item.overrides) || calendarValidation(item.workingStart, item.workingEnd, item.overrides)) {
    throw new Error('工作日历响应不完整。')
  }
  return item
}
