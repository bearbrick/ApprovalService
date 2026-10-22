namespace ApprovalService.Domain.Notifications.Calendar;

/// <summary>
/// 覆盖某一天的默认周末规则，既可登记节假日，也可登记调休工作日。
/// </summary>
public sealed class WorkCalendarDayOverride
{
    /// <summary>
    /// 创建一天的覆盖设置。
    /// </summary>
    /// <param name="date">按工作日历所在时区解释的本地日期。</param>
    /// <param name="isWorkingDay">当天是否为工作日。</param>
    public WorkCalendarDayOverride(DateOnly date, bool isWorkingDay)
    {
        Date = date;
        IsWorkingDay = isWorkingDay;
    }

    /// <summary>
    /// 工作日历所在时区的本地日期。
    /// </summary>
    public DateOnly Date
    {
        get;
    }

    /// <summary>
    /// 该日期是否覆盖为工作日。
    /// </summary>
    public bool IsWorkingDay
    {
        get;
    }
}
