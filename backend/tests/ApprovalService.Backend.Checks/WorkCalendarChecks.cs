using ApprovalService.Domain.Notifications.Calendar;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 工作日历在普通周、节假日、调休以及时区切换时的场景检查。
/// </summary>
internal static class WorkCalendarChecks
{
    /// <summary>
    /// 检查日历覆盖、跨工作日计算和时区投影。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        CheckWorkingDates(check);
        CheckConfiguration(check);
        CheckTimeZones(check);
    }

    /// <summary>
    /// 检查周末、节假日和调休工作日的优先级。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckWorkingDates(Action<string, bool> check)
    {
        var friday = new DateOnly(2026, 10, 16);
        var ordinary = new WorkCalendarRules([]);
        check("工作日历默认跳过周六和周日", ordinary.GetWorkingDateAfter(friday, 1) == new DateOnly(2026, 10, 19));
        check("工作日历按起点之后的工作日计数", ordinary.GetWorkingDateAfter(friday, 2) == new DateOnly(2026, 10, 20));

        var adjusted = new WorkCalendarRules([
            new WorkCalendarDayOverride(new DateOnly(2026, 10, 18), true),
            new WorkCalendarDayOverride(new DateOnly(2026, 10, 19), false),
        ]);
        check("调休周日覆盖默认周末", adjusted.IsWorkingDay(new DateOnly(2026, 10, 18)) &&
            adjusted.GetWorkingDateAfter(friday, 1) == new DateOnly(2026, 10, 18));
        check("节假日覆盖默认周一并保持连续计数", !adjusted.IsWorkingDay(new DateOnly(2026, 10, 19)) &&
            adjusted.GetWorkingDateAfter(friday, 3) == new DateOnly(2026, 10, 21));
        check("非工作日起算时仍从后续第一工作日计数", adjusted.GetWorkingDateAfter(new DateOnly(2026, 10, 19), 1) ==
            new DateOnly(2026, 10, 20));
    }

    /// <summary>
    /// 检查配置输入被复制、重复日期被拒绝。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckConfiguration(Action<string, bool> check)
    {
        var sunday = new DateOnly(2026, 10, 18);
        var supplied = new List<WorkCalendarDayOverride> { new(sunday, true) };
        var calendar = new WorkCalendarRules(supplied);
        supplied.Clear();
        check("日历快照不受调用方后续修改影响", calendar.IsWorkingDay(sunday));

        var duplicateRejected = false;
        try
        {
            _ = new WorkCalendarRules([new WorkCalendarDayOverride(sunday, true), new WorkCalendarDayOverride(sunday, false)]);
        }
        catch (ArgumentException)
        {
            duplicateRejected = true;
        }

        check("工作日历拒绝重复日期配置", duplicateRejected);
    }

    /// <summary>
    /// 检查时间点按目标时区及夏令时规则投影成本地日期。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckTimeZones(Action<string, bool> check)
    {
        var eastChina = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var instant = new DateTimeOffset(2026, 3, 9, 4, 30, 0, TimeSpan.Zero);
        check("工作日期采用指定时区而非服务器时区", WorkCalendarRules.GetLocalDate(instant, eastChina) ==
            new DateOnly(2026, 3, 9));
        check("夏令时后按时区规则跨本地午夜", WorkCalendarRules.GetLocalDate(instant, newYork) ==
            new DateOnly(2026, 3, 9));

        var beforeSwitch = new DateTimeOffset(2026, 3, 8, 4, 30, 0, TimeSpan.Zero);
        check("夏令时前同一 UTC 时刻仍属前一工作日期", WorkCalendarRules.GetLocalDate(beforeSwitch, newYork) ==
            new DateOnly(2026, 3, 7));
    }
}
