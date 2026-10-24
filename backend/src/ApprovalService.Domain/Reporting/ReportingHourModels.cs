namespace ApprovalService.Domain.Reporting;

/// <summary>
/// 四种小时指标的稳定编码，供后续持久化切片复用。
/// </summary>
public static class ReportingHourMetricKinds
{
    /// <summary>
    /// 审批单发起。
    /// </summary>
    public const string Started = "started";

    /// <summary>
    /// 审批单结束。
    /// </summary>
    public const string Completed = "completed";

    /// <summary>
    /// 任务处理完成。
    /// </summary>
    public const string TaskCompleted = "taskCompleted";

    /// <summary>
    /// 任务到期。
    /// </summary>
    public const string TaskDue = "taskDue";
}

/// <summary>
/// 同一 UTC 小时及相同统计维度的内部指标；保密桶的流程编码仅供服务端筛选，禁止输出明细。
/// </summary>
public sealed class ReportingHourBucket
{
    /// <summary>
    /// UTC 小时起点。
    /// </summary>
    public DateTimeOffset HourUtc
    {
        get; set;
    }

    /// <summary>
    /// 指标种类。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码快照；保密流程不允许向查询调用方公开此维度。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 来源接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;

    /// <summary>
    /// 发起部门 ID 快照。
    /// </summary>
    public string? InitiatorDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 锁定流程版本的保密标记。
    /// </summary>
    public bool IsConfidential
    {
        get; set;
    }

    /// <summary>
    /// 仅非保密任务指标带节点 ID。
    /// </summary>
    public string? NodeId
    {
        get; set;
    }

    /// <summary>
    /// 仅非保密任务指标带实际处理人 ID。
    /// </summary>
    public string? AssigneeId
    {
        get; set;
    }

    /// <summary>
    /// 该分组的事件或任务数量。
    /// </summary>
    public int Count
    {
        get; set;
    }

    /// <summary>
    /// 已结束审批单中的通过数。
    /// </summary>
    public int ApprovedCount
    {
        get; set;
    }

    /// <summary>
    /// 已结束审批单中的拒绝数。
    /// </summary>
    public int RejectedCount
    {
        get; set;
    }

    /// <summary>
    /// 已结束审批单中的撤回数。
    /// </summary>
    public int WithdrawnCount
    {
        get; set;
    }

    /// <summary>
    /// 已到期任务中的逾期数。
    /// </summary>
    public int OverdueCount
    {
        get; set;
    }

    /// <summary>
    /// 经验证的时长样本，升序保存以供跨小时精确计算 P90；保密桶为空。
    /// </summary>
    public IReadOnlyList<double> DurationSamplesMinutes { get; set; } = [];
}

/// <summary>
/// 一个审批单可供增量投影规划的最小事件水位。
/// </summary>
public sealed class ReportingProjectionSource
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 审批单已提交的最后事件序号。
    /// </summary>
    public long LastEventSequence
    {
        get; set;
    }

    /// <summary>
    /// 该审批单所有已提交事件中的最大发生时间；零事件时为空。
    /// </summary>
    public DateTimeOffset? MaxEventOccurredAtUtc
    {
        get; set;
    }
}

/// <summary>
/// 已投影审批单的持久水位候选值。
/// </summary>
public sealed class ReportingProjectionCheckpoint
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 已投影的最后事件序号。
    /// </summary>
    public long LastEventSequence
    {
        get; set;
    }
}

/// <summary>
/// 投影计划中的替换操作；调用方须在同一事务中删除旧事实并写入新事实及水位。
/// </summary>
public sealed class ReportingProjectionReplacement
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 新的事件序号水位。
    /// </summary>
    public long LastEventSequence
    {
        get; set;
    }
}

/// <summary>
/// 已关闭小时的确定性增量规划。
/// </summary>
public sealed class ReportingProjectionPlan
{
    /// <summary>
    /// 需要替换整个审批单旧投影的项目。
    /// </summary>
    public IReadOnlyList<ReportingProjectionReplacement> Replace { get; set; } = [];

    /// <summary>
    /// 事件发生时间尚未落入已关闭小时的审批单 ID。
    /// </summary>
    public IReadOnlyList<string> DeferredInstanceIds { get; set; } = [];
}
