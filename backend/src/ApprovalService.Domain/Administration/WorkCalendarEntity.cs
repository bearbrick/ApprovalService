namespace ApprovalService.Domain.Administration;

/// <summary>
/// 在线保存的全局工作日历；部署时区不写入此表。
/// </summary>
public sealed class WorkCalendarEntity
{
    /// <summary>
    /// 全局唯一设置键。
    /// </summary>
    public string Id { get; set; } = "global";

    /// <summary>
    /// 乐观并发修订。
    /// </summary>
    public long Revision
    {
        get; set;
    }

    /// <summary>
    /// HH:mm 开工时刻。
    /// </summary>
    public string WorkingStart { get; set; } = string.Empty;

    /// <summary>
    /// HH:mm 结束时刻。
    /// </summary>
    public string WorkingEnd { get; set; } = string.Empty;

    /// <summary>
    /// 已校验并按日期排序的覆盖 JSON。
    /// </summary>
    public string OverridesJson { get; set; } = "[]";
}
