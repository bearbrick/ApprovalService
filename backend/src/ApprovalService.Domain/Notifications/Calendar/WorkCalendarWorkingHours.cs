namespace ApprovalService.Domain.Notifications.Calendar;

/// <summary>
/// 一个工作日内可累计节点时限的本地时段。
/// </summary>
public sealed class WorkCalendarWorkingHours
{
    /// <summary>
    /// 默认每天九点到十八点累计工作分钟。
    /// </summary>
    public static WorkCalendarWorkingHours Default
    {
        get;
    } = new(new TimeOnly(9, 0), new TimeOnly(18, 0));

    /// <summary>
    /// 建立一个当天开始、当天结束的工作时段。
    /// </summary>
    /// <param name="start">工作日历所在时区的开始时间。</param>
    /// <param name="end">工作日历所在时区的结束时间，必须晚于开始时间。</param>
    public WorkCalendarWorkingHours(TimeOnly start, TimeOnly end)
    {
        if (end <= start)
        {
            throw new ArgumentException("工作时段结束时间必须晚于开始时间。", nameof(end));
        }

        Start = start;
        End = end;
    }

    /// <summary>
    /// 每个工作日开始累计的本地时间。
    /// </summary>
    public TimeOnly Start
    {
        get;
    }

    /// <summary>
    /// 每个工作日停止累计的本地时间。
    /// </summary>
    public TimeOnly End
    {
        get;
    }
}
