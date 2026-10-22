using ApprovalService.Domain.Notifications.Calendar;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 工作日历在普通周、节假日、调休、节点期限和时区切换时的场景检查。
/// </summary>
internal static class WorkCalendarChecks
{
    /// <summary>
    /// 检查日历覆盖、节点期限和时区投影。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        CheckWorkingDates(check);
        CheckConfiguration(check);
        CheckTimeZones(check);
        CheckDueAt(check);
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

    /// <summary>
    /// 检查节点分钟时限仅在配置的工作时段内累计。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckDueAt(Action<string, bool> check)
    {
        var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var ordinary = new WorkCalendarRules([]);
        var friday = new DateTimeOffset(2026, 10, 16, 17, 0, 0, TimeSpan.FromHours(8));
        check("节点时限跨过周末后继续累计工作分钟",
            ordinary.GetDueAt(friday, 120, china) == new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(8)));
        check("到达工作时段末尾恰好完成时不顺延",
            ordinary.GetDueAt(friday, 60, china) == new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.FromHours(8)));
        check("工作时段外开始时从下一工作日开工时间计时",
            ordinary.GetDueAt(friday.AddHours(2), 30, china) ==
            new DateTimeOffset(2026, 10, 19, 9, 30, 0, TimeSpan.FromHours(8)));

        var adjusted = new WorkCalendarRules([
            new WorkCalendarDayOverride(new DateOnly(2026, 10, 18), true),
            new WorkCalendarDayOverride(new DateOnly(2026, 10, 19), false),
        ]);
        check("调休计入且法定假日跳过，均按工作分钟累计",
            adjusted.GetDueAt(friday.AddMinutes(30), 600, china) ==
            new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.FromHours(8)));

        var shortHours = new WorkCalendarWorkingHours(new TimeOnly(8, 0), new TimeOnly(12, 0));
        check("自定义工作时段改变每日可累计分钟数",
            ordinary.GetDueAt(new DateTimeOffset(2026, 10, 16, 11, 0, 0, TimeSpan.FromHours(8)), 180, china, shortHours) ==
            new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(8)));

        var invalidRejected = false;
        try
        {
            _ = new WorkCalendarWorkingHours(new TimeOnly(18, 0), new TimeOnly(9, 0));
        }
        catch (ArgumentException)
        {
            invalidRejected = true;
        }

        check("工作时段拒绝倒置的开始和结束时间", invalidRejected);

        var zeroRejected = false;
        try
        {
            _ = ordinary.GetDueAt(friday, 0, china);
        }
        catch (ArgumentOutOfRangeException)
        {
            zeroRejected = true;
        }

        check("节点期限拒绝零分钟时限", zeroRejected);

        var newYork = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var springSunday = new WorkCalendarRules([new WorkCalendarDayOverride(new DateOnly(2026, 3, 8), true)]);
        var dawnHours = new WorkCalendarWorkingHours(new TimeOnly(1, 0), new TimeOnly(4, 0));
        check("夏令时春季缺口只累计实际经过的工作分钟",
            springSunday.GetDueAt(new DateTimeOffset(2026, 3, 8, 1, 0, 0, TimeSpan.FromHours(-5)), 120, newYork, dawnHours) ==
            new DateTimeOffset(2026, 3, 8, 4, 0, 0, TimeSpan.FromHours(-4)));

        var autumnSunday = new WorkCalendarRules([new WorkCalendarDayOverride(new DateOnly(2026, 11, 1), true)]);
        var autumnHours = new WorkCalendarWorkingHours(new TimeOnly(1, 0), new TimeOnly(3, 0));
        check("夏令时秋季重复小时按照实际经过的分钟累计",
            autumnSunday.GetDueAt(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.FromHours(-4)), 120, newYork, autumnHours) ==
            new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.FromHours(-5)));
    }
}
