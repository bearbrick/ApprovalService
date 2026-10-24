namespace ApprovalService.Domain.Reporting;

/// <summary>
/// 统计指标的查询口径；调用者须提供覆盖筛选范围的完整事实集合。
/// </summary>
public sealed class ReportingMetricsQuery
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
    /// 待办和到期状态的观察时间，必须为 UTC。
    /// </summary>
    public DateTimeOffset AsOfUtc
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
    /// 可选接入应用编码。
    /// </summary>
    public string? ClientAppCode
    {
        get; set;
    }

    /// <summary>
    /// 可选的发起部门编码快照。
    /// </summary>
    public string? InitiatorDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 可选的多个直接负责部门 ID；空集合表示无可见部门。
    /// </summary>
    public IReadOnlyList<string>? InitiatorDepartmentIds
    {
        get; set;
    }
}

/// <summary>
/// 从完整事实集合计算出的内部报表指标，不含审批单内容。
/// </summary>
public sealed class ReportingMetrics
{
    /// <summary>
    /// 按流程汇总。
    /// </summary>
    public IReadOnlyList<ReportingFlowMetric> Flows { get; set; } = [];

    /// <summary>
    /// 按流程和节点汇总。
    /// </summary>
    public IReadOnlyList<ReportingNodeMetric> Nodes { get; set; } = [];

    /// <summary>
    /// 按实际处理人汇总；对外使用前必须执行管理范围和保密授权。
    /// </summary>
    public IReadOnlyList<ReportingPersonMetric> People { get; set; } = [];
}

/// <summary>
/// 流程发起与完成指标。
/// </summary>
public sealed class ReportingFlowMetric
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 时间段内的发起量。
    /// </summary>
    public int StartedCount
    {
        get; set;
    }

    /// <summary>
    /// 时间段内的结束量。
    /// </summary>
    public int CompletedCount
    {
        get; set;
    }

    /// <summary>
    /// 时间段内已通过数量。
    /// </summary>
    public int ApprovedCount
    {
        get; set;
    }

    /// <summary>
    /// 时间段内已拒绝数量。
    /// </summary>
    public int RejectedCount
    {
        get; set;
    }

    /// <summary>
    /// 时间段内已撤回数量。
    /// </summary>
    public int WithdrawnCount
    {
        get; set;
    }

    /// <summary>
    /// 已结束审批单的平均历时分钟数；无样本时为空。
    /// </summary>
    public double? AverageDurationMinutes
    {
        get; set;
    }

    /// <summary>
    /// 已结束审批单历时的最近秩 P90 分钟数；无样本时为空。
    /// </summary>
    public double? P90DurationMinutes
    {
        get; set;
    }
}

/// <summary>
/// 节点待办处理与到期指标。
/// </summary>
public sealed class ReportingNodeMetric
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 时间段内完成的任务数。
    /// </summary>
    public int CompletedCount
    {
        get; set;
    }

    /// <summary>
    /// 有真实处理起点的任务平均历时分钟数；无样本时为空。
    /// </summary>
    public double? AverageDurationMinutes
    {
        get; set;
    }

    /// <summary>
    /// 到期时间在筛选区间内且到观察时间已经到期的任务数。
    /// </summary>
    public int DueCount
    {
        get; set;
    }

    /// <summary>
    /// 已到期任务中逾期完成或仍待处理的数量。
    /// </summary>
    public int OverdueCount
    {
        get; set;
    }
}

/// <summary>
/// 实际处理人的任务指标。
/// </summary>
public sealed class ReportingPersonMetric
{
    /// <summary>
    /// 实际处理人工号。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;

    /// <summary>
    /// 当前待处理任务数；排队任务不计入。
    /// </summary>
    public int PendingCount
    {
        get; set;
    }

    /// <summary>
    /// 时间段内完成的任务数。
    /// </summary>
    public int CompletedCount
    {
        get; set;
    }

    /// <summary>
    /// 有真实处理起点的任务平均历时分钟数；无样本时为空。
    /// </summary>
    public double? AverageDurationMinutes
    {
        get; set;
    }

    /// <summary>
    /// 已到期任务中逾期完成或仍待处理的次数。
    /// </summary>
    public int OverdueCount
    {
        get; set;
    }
}
