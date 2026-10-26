namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 逐日本地日期的工作状态覆盖。
/// </summary>
public sealed class WorkCalendarOverride
{
    /// <summary>
    /// 严格 yyyy-MM-dd 本地日期。
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// 是否为工作日；提交时必须明确指定。
    /// </summary>
    public bool? IsWorkingDay
    {
        get; set;
    }
}

/// <summary>
/// 当前生效的全局工作日历，时区由部署固定。
/// </summary>
public sealed class WorkCalendarResponse
{
    /// <summary>
    /// 保存修订；未在线保存时为零。
    /// </summary>
    public long Revision
    {
        get; set;
    }

    /// <summary>
    /// 只读部署时区标识。
    /// </summary>
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary>
    /// 每个工作日的开工时刻，格式 HH:mm。
    /// </summary>
    public string WorkingStart { get; set; } = string.Empty;

    /// <summary>
    /// 每个工作日的结束时刻，格式 HH:mm。
    /// </summary>
    public string WorkingEnd { get; set; } = string.Empty;

    /// <summary>
    /// 节假日和调休覆盖；未列出的日期按周一至周五工作。
    /// </summary>
    public List<WorkCalendarOverride> Overrides { get; set; } = [];
}

/// <summary>
/// 按预期修订整体替换工作日历的请求。
/// </summary>
public sealed class UpdateWorkCalendarRequest
{
    /// <summary>
    /// 编辑时读取的修订。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 每日开工时刻，格式 HH:mm。
    /// </summary>
    public string WorkingStart { get; set; } = string.Empty;

    /// <summary>
    /// 每日结束时刻，格式 HH:mm。
    /// </summary>
    public string WorkingEnd { get; set; } = string.Empty;

    /// <summary>
    /// 完整的逐日覆盖清单。
    /// </summary>
    public List<WorkCalendarOverride> Overrides { get; set; } = [];

    /// <summary>
    /// 管理员填写的变更原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
