using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 启动时验证的部署默认值；向调用方返回隔离副本。
/// </summary>
/// <param name="value">部署日历快照。</param>
public sealed class WorkCalendarDefaults(WorkCalendarResponse value)
{
    /// <summary>
    /// 创建修订零的配置副本。
    /// </summary>
    /// <returns>可独立使用的默认日历。</returns>
    public WorkCalendarResponse Read() => new()
    {
        TimeZoneId = value.TimeZoneId,
        WorkingStart = value.WorkingStart,
        WorkingEnd = value.WorkingEnd,
        Overrides = value.Overrides.Select(item => new WorkCalendarOverride
        {
            Date = item.Date,
            IsWorkingDay = item.IsWorkingDay,
        }).ToList(),
    };
}
