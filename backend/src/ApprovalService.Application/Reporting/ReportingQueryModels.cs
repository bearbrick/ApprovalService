using ApprovalService.Domain.Reporting;

namespace ApprovalService.Application.Reporting;

/// <summary>
/// 内部报表查询条件；观察时刻由服务端决定，不能由调用方指定。
/// </summary>
public sealed class ReportingQueryInput
{
    /// <summary>
    /// UTC 时间下界，包含。
    /// </summary>
    public DateTimeOffset FromUtc
    {
        get; set;
    }

    /// <summary>
    /// UTC 时间上界，不包含。
    /// </summary>
    public DateTimeOffset UntilUtc
    {
        get; set;
    }

    /// <summary>
    /// 可选流程编码。
    /// </summary>
    public string? FlowCode
    {
        get; set;
    }

    /// <summary>
    /// 可选来源应用编码。
    /// </summary>
    public string? ClientAppCode
    {
        get; set;
    }

    /// <summary>
    /// 可选发起部门 ID 快照。
    /// </summary>
    public string? InitiatorDepartmentId
    {
        get; set;
    }
}

/// <summary>
/// 仅供内部使用的组织管理员报表结果；保密流程不携带流程和人员维度。
/// </summary>
public sealed class ReportingQueryDto
{
    /// <summary>
    /// 非保密流程的流程、节点和人员指标。
    /// </summary>
    public ReportingMetrics VisibleMetrics { get; set; } = new();

    /// <summary>
    /// 保密流程在时间段内的匿名发起量。
    /// </summary>
    public int ConfidentialStartedCount
    {
        get; set;
    }

    /// <summary>
    /// 保密流程在时间段内的匿名结束量。
    /// </summary>
    public int ConfidentialCompletedCount
    {
        get; set;
    }
}
