using ApprovalService.Domain.Notifications.Calendar;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 从固定期限向前累计工作分钟，求出到期前提醒时刻。
/// </summary>
public sealed class DeadlineReminderClock
{
    /// <summary>
    /// 防止错误日历导致无限扫描。
    /// </summary>
    private const int MaximumScanDays = 36600;

    /// <summary>
    /// 发布时使用的工作日历规则。
    /// </summary>
    private readonly WorkCalendarRules calendar;

    /// <summary>
    /// 日历时区。
    /// </summary>
    private readonly TimeZoneInfo timeZone;

    /// <summary>
    /// 每日工作时段。
    /// </summary>
    private readonly WorkCalendarWorkingHours workingHours;

    /// <summary>
    /// 建立固定配置的工作分钟逆向计算器。
    /// </summary>
    /// <param name="calendar">节假日和调休规则。</param>
    /// <param name="timeZone">工作日历时区。</param>
    /// <param name="workingHours">每日工作时段。</param>
    public DeadlineReminderClock(WorkCalendarRules calendar, TimeZoneInfo timeZone,
        WorkCalendarWorkingHours workingHours)
    {
        this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        this.timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        this.workingHours = workingHours ?? throw new ArgumentNullException(nameof(workingHours));
    }

    /// <summary>
    /// 从到期时刻向前累计指定的实际工作分钟。
    /// </summary>
    /// <param name="dueAt">任务已固定的到期绝对时刻。</param>
    /// <param name="workingMinutes">提前的工作分钟数，必须大于零。</param>
    /// <returns>提前提醒的绝对时刻。</returns>
    public DateTimeOffset GetBeforeDueAt(DateTimeOffset dueAt, int workingMinutes)
    {
        if (workingMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workingMinutes), "提醒提前量必须大于零分钟。");
        }

        var cursor = dueAt.ToUniversalTime();
        var date = WorkCalendarRules.GetLocalDate(cursor, timeZone);
        var remaining = TimeSpan.FromMinutes(workingMinutes);
        for (var scanned = 0; scanned < MaximumScanDays; scanned++)
        {
            if (calendar.IsWorkingDay(date))
            {
                var begin = ResolveBoundary(date.ToDateTime(workingHours.Start), true);
                var end = ResolveBoundary(date.ToDateTime(workingHours.End), false);
                var effectiveEnd = cursor < end ? cursor : end;
                if (effectiveEnd > begin)
                {
                    var available = effectiveEnd - begin;
                    if (remaining <= available)
                    {
                        return TimeZoneInfo.ConvertTime(effectiveEnd - remaining, timeZone);
                    }

                    remaining -= available;
                }
            }

            // 非工作日和工作时段以外不扣减提前量；整日不足时回退上一日。
            if (date == DateOnly.MinValue)
            {
                break;
            }

            date = date.AddDays(-1);
            cursor = DateTimeOffset.MaxValue;
        }

        throw new ArgumentException("工作日历在可计算的日期范围内没有足够的工作分钟。", nameof(workingMinutes));
    }

    /// <summary>
    /// 按正向期限计算的相同规则处理夏令时缺口和重复时刻。
    /// </summary>
    /// <param name="localTime">未指定时区的本地边界。</param>
    /// <param name="isStart">开始边界取重复时刻中较早的一次。</param>
    /// <returns>UTC 边界。</returns>
    private DateTimeOffset ResolveBoundary(DateTime localTime, bool isStart)
    {
        while (timeZone.IsInvalidTime(localTime))
        {
            localTime = localTime.AddMinutes(1);
        }

        TimeSpan offset;
        if (timeZone.IsAmbiguousTime(localTime))
        {
            var offsets = timeZone.GetAmbiguousTimeOffsets(localTime);
            offset = isStart ? offsets.Max() : offsets.Min();
        }
        else
        {
            offset = timeZone.GetUtcOffset(localTime);
        }

        return new DateTimeOffset(localTime, offset).ToUniversalTime();
    }
}
