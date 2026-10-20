namespace ApprovalService.Domain.Notifications.Calendar;

/// <summary>
/// 工作日历的确定性日期规则；调用方负责提供本地节假日与调休清单。
/// </summary>
public sealed class WorkCalendarRules
{
    /// <summary>
    /// 防止错误配置造成不受限的日期扫描。
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
}
