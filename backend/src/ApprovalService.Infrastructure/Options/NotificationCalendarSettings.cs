using System.Globalization;
using ApprovalService.Application.Administration;
using ApprovalService.Application.Notifications;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Notifications.Calendar;
using Microsoft.Extensions.Configuration;

namespace ApprovalService.Infrastructure.Options;

/// <summary>
/// 从部署配置建立工作日历快照，启动时拒绝无效时区和日期。
/// </summary>
public static class NotificationCalendarSettings
{
    /// <summary>
    /// 读取时区、工作时段与节假日覆盖并建立期限策略。
    /// </summary>
    /// <param name="configuration">应用配置。</param>
    /// <returns>不可变的期限计算策略。</returns>
    public static TaskDeadlinePolicy Create(IConfiguration configuration) => WorkCalendarAppService.CreatePolicy(Read(configuration));

    /// <summary>
    /// 读取并验证完整的部署日历，供空库回退和只读时区使用。
    /// </summary>
    /// <param name="configuration">应用配置。</param>
    /// <returns>修订零的部署日历。</returns>
    public static WorkCalendarResponse Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("ApprovalService:Notifications:Calendar");
        var timeZoneId = section["TimeZoneId"] ?? "Asia/Shanghai";
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException error)
        {
            throw new InvalidOperationException("通知工作日历的时区不存在。", error);
        }
        catch (InvalidTimeZoneException error)
        {
            throw new InvalidOperationException("通知工作日历的时区无效。", error);
        }

        if (!TimeOnly.TryParseExact(section["WorkingStart"] ?? "09:00", "HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(section["WorkingEnd"] ?? "18:00", "HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end <= start)
        {
            throw new InvalidOperationException("通知工作日历的工作时段必须是 HH:mm 且结束晚于开始。");
        }

        var overrides = new List<WorkCalendarDayOverride>();
        foreach (var day in section.GetSection("Overrides").GetChildren())
        {
            if (!DateOnly.TryParseExact(day.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) || !bool.TryParse(day.Value, out var isWorkingDay))
            {
                throw new InvalidOperationException("通知工作日历的日期覆盖须使用 yyyy-MM-dd 和 true/false。");
            }

            overrides.Add(new WorkCalendarDayOverride(date, isWorkingDay));
        }

        return new WorkCalendarResponse
        {
            TimeZoneId = timeZone.Id,
            WorkingStart = start.ToString("HH:mm", CultureInfo.InvariantCulture),
            WorkingEnd = end.ToString("HH:mm", CultureInfo.InvariantCulture),
            Overrides = overrides.Select(item => new WorkCalendarOverride
            {
                Date = item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IsWorkingDay = item.IsWorkingDay,
            }).OrderBy(item => item.Date, StringComparer.Ordinal).ToList(),
        };
    }
}
