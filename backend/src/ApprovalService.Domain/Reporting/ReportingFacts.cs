namespace ApprovalService.Domain.Reporting;

/// <summary>
/// 统计事实筛选条件；时间和部门均明确指审批单发起时的快照。
/// </summary>
public sealed class ReportingFactsQuery
{
    /// <summary>
    /// 审批单发起时间下界，包含，必须为 UTC。
    /// </summary>
    public DateTimeOffset InstanceStartedFromUtc
    {
        get; set;
    }

    /// <summary>
    /// 审批单发起时间上界，不包含，必须为 UTC。
    /// </summary>
    public DateTimeOffset InstanceStartedUntilUtc
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
    /// 可选发起部门编码快照。
    /// </summary>
    public string? InitiatorDepartmentId
    {
        get; set;
    }
}

/// <summary>
/// 一次查询中读取的原始事实；不包含审批数据和保密详情。
/// </summary>
public sealed class ReportingFacts
{
    /// <summary>
    /// 审批单事实。
    /// </summary>
    public IReadOnlyList<ReportingInstanceFact> Instances { get; set; } = [];

    /// <summary>
    /// 任务事实。
    /// </summary>
    public IReadOnlyList<ReportingTaskFact> Tasks { get; set; } = [];

    /// <summary>
    /// 任务创建事件事实；没有事件的任务不会生成虚构的起点。
    /// </summary>
    public IReadOnlyList<ReportingTaskCreatedFact> TaskCreatedEvents { get; set; } = [];

    /// <summary>
    /// 依次审批等待结束时的任务激活事件；初始即待办的任务没有此事件。
    /// </summary>
    public IReadOnlyList<ReportingTaskActivatedFact> TaskActivatedEvents { get; set; } = [];
}

/// <summary>
/// 审批单统计所需的最小事实。
/// </summary>
public sealed class ReportingInstanceFact
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码快照。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 流程版本 ID。
    /// </summary>
    public string FlowVersionId { get; set; } = string.Empty;

    /// <summary>
    /// 来源应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;

    /// <summary>
    /// 发起部门编码快照。
    /// </summary>
    public string? InitiatorDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 审批单状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 发起时间。
    /// </summary>
    public DateTimeOffset StartedAt
    {
        get; set;
    }

    /// <summary>
    /// 结束时间。
    /// </summary>
    public DateTimeOffset? EndedAt
    {
        get; set;
    }
}

/// <summary>
/// 审批任务统计所需的最小事实。
/// </summary>
public sealed class ReportingTaskFact
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 实际处理人工号。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;

    /// <summary>
    /// 代理任务对应的原审批人工号。
    /// </summary>
    public string? DelegatedForId
    {
        get; set;
    }

    /// <summary>
    /// 任务状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 任务到期时间。
    /// </summary>
    public DateTimeOffset? DueAt
    {
        get; set;
    }

    /// <summary>
    /// 任务完成时间。
    /// </summary>
    public DateTimeOffset? CompletedAt
    {
        get; set;
    }
}

/// <summary>
/// 只追加日志中的任务创建事件事实。
/// </summary>
public sealed class ReportingTaskCreatedFact
{
    /// <summary>
    /// 事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string? NodeId
    {
        get; set;
    }

    /// <summary>
    /// 事件发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}

/// <summary>
/// 只追加日志中的任务激活事件事实，用于排除依次审批的排队时间。
/// </summary>
public sealed class ReportingTaskActivatedFact
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 事件发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}
