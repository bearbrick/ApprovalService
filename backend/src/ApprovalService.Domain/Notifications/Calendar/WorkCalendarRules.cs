namespace ApprovalService.Domain.Notifications.Calendar;

/// <summary>
/// 工作日历的确定性日期与期限规则；调用方负责提供节假日、调休和时区配置。
/// </summary>
public sealed class WorkCalendarRules
{
    /// <summary>
    /// 防止错误配置造成不受限的日期扫描和期限计算。
    /// </summary>
    private const int MaximumScanDays = 36600;

    /// <summary>
    /// 按日期索引的节假日和调休覆盖。
    /// </summary>
    private readonly IReadOnlyDictionary<DateOnly, bool> overrides;

    /// <summary>
    /// 建立一份不可变的日历快照。
    /// </summary>
    /// <param name="dayOverrides">节假日与调休工作日；未列出的周一至周五是工作日。</param>
    public WorkCalendarRules(IEnumerable<WorkCalendarDayOverride> dayOverrides)
    {
        ArgumentNullException.ThrowIfNull(dayOverrides);
        var values = new Dictionary<DateOnly, bool>();
        foreach (var item in dayOverrides)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!values.TryAdd(item.Date, item.IsWorkingDay))
            {
                throw new ArgumentException("工作日历中同一日期不能重复配置。", nameof(dayOverrides));
            }
        }

        // 复制输入，后续配置变动不会悄悄改变已经计算的期限。
        overrides = values;
    }

    /// <summary>
    /// 判断给定的本地日期是否为工作日。
    /// </summary>
    /// <param name="date">日历所在时区的本地日期。</param>
    /// <returns>覆盖设置优先，否则周一至周五返回真。</returns>
    public bool IsWorkingDay(DateOnly date)
    {
        if (overrides.TryGetValue(date, out var isWorkingDay))
        {
            return isWorkingDay;
        }

        return date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    /// <summary>
    /// 从指定日期之后寻找第 N 个工作日，起点本身不计入。
    /// </summary>
    /// <param name="startDate">不计入结果的起始本地日期。</param>
    /// <param name="workingDays">向后跨过的工作日数，必须大于零。</param>
    /// <returns>第 N 个工作日的本地日期。</returns>
    public DateOnly GetWorkingDateAfter(DateOnly startDate, int workingDays)
    {
        if (workingDays is < 1 or > MaximumScanDays)
        {
            throw new ArgumentOutOfRangeException(nameof(workingDays), "工作日数必须在 1 到 36600 之间。");
        }

        var date = startDate;
        var remaining = workingDays;
        for (var scanned = 0; scanned < MaximumScanDays; scanned++)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date))
            {
                remaining--;
                if (remaining == 0)
                {
                    return date;
                }
            }
        }

        throw new ArgumentException("工作日历在可计算的日期范围内没有足够的工作日。", nameof(workingDays));
    }

    /// <summary>
    /// 将时间点投影到日历时区的本地日期；不用服务器本地时区或固定 UTC 偏移。
    /// </summary>
    /// <param name="instant">带偏移的绝对时间点。</param>
    /// <param name="calendarTimeZone">工作日历所属的时区。</param>
    /// <returns>该时间点在日历时区的本地日期。</returns>
    public static DateOnly GetLocalDate(DateTimeOffset instant, TimeZoneInfo calendarTimeZone)
    {
        ArgumentNullException.ThrowIfNull(calendarTimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, calendarTimeZone).DateTime);
    }

    /// <summary>
    /// 从任务开始时间累计工作分钟，返回所在时区的到期时刻。
    /// </summary>
    /// <param name="startAt">任务开始的绝对时间点。</param>
    /// <param name="dueMinutes">需要累计的工作分钟数，必须大于零。</param>
    /// <param name="calendarTimeZone">工作日历所属的时区。</param>
    /// <param name="workingHours">每天的工作时段；空值使用九点到十八点。</param>
    /// <returns>累计完工作分钟时，在日历时区表示的时间点。</returns>
    public DateTimeOffset GetDueAt(
        DateTimeOffset startAt,
        int dueMinutes,
        TimeZoneInfo calendarTimeZone,
        WorkCalendarWorkingHours? workingHours = null)
    {
        if (dueMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dueMinutes), "节点时限必须大于零分钟。");
        }

        ArgumentNullException.ThrowIfNull(calendarTimeZone);
        var hours = workingHours ?? WorkCalendarWorkingHours.Default;
        var cursor = startAt.ToUniversalTime();
        var date = GetLocalDate(cursor, calendarTimeZone);
        var remaining = TimeSpan.FromMinutes(dueMinutes);

        for (var scanned = 0; scanned < MaximumScanDays; scanned++)
        {
            if (IsWorkingDay(date))
            {
                var begin = ResolveBoundary(date.ToDateTime(hours.Start), calendarTimeZone, true);
                var end = ResolveBoundary(date.ToDateTime(hours.End), calendarTimeZone, false);
                var effectiveStart = cursor > begin ? cursor : begin;
                if (effectiveStart < end)
                {
                    var available = end - effectiveStart;
                    if (remaining <= available)
                    {
                        return TimeZoneInfo.ConvertTime(effectiveStart + remaining, calendarTimeZone);
                    }

                    remaining -= available;
                }
            }

            // 非工作日期和每天工作时段之外不消耗分钟数。
            if (date == DateOnly.MaxValue)
            {
                break;
            }

            date = date.AddDays(1);
        }

        throw new ArgumentException("工作日历在可计算的日期范围内没有足够的工作分钟。", nameof(dueMinutes));
    }

    /// <summary>
    /// 将本地时段边界确定地转换为绝对时间，处理夏令时缺口与重复时刻。
    /// </summary>
    /// <param name="localTime">未指定时区的本地日期时间。</param>
    /// <param name="calendarTimeZone">工作日历所属的时区。</param>
    /// <param name="isStart">开始边界选重复时刻中较早的一次，结束边界选较晚的一次。</param>
    /// <returns>边界对应的 UTC 时间点。</returns>
    private static DateTimeOffset ResolveBoundary(DateTime localTime, TimeZoneInfo calendarTimeZone, bool isStart)
    {
        // 春季跳时形成的无效本地时刻顺延到第一个有效分钟。
        while (calendarTimeZone.IsInvalidTime(localTime))
        {
            localTime = localTime.AddMinutes(1);
        }

        TimeSpan offset;
        if (calendarTimeZone.IsAmbiguousTime(localTime))
        {
            var offsets = calendarTimeZone.GetAmbiguousTimeOffsets(localTime);
            offset = isStart ? offsets.Max() : offsets.Min();
        }
        else
        {
            offset = calendarTimeZone.GetUtcOffset(localTime);
        }

        return new DateTimeOffset(localTime, offset).ToUniversalTime();
    }
}
