namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 报表工作簿的固定文件约定。
/// </summary>
public static class AdminReportExportFile
{
    /// <summary>
    /// Excel Open XML 文件的媒体类型。
    /// </summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// 下载文件名。
    /// </summary>
    public const string FileName = "approval-report.xlsx";
}

/// <summary>
/// 四条报表 GET 请求共用的查询参数名称。
/// </summary>
public static class AdminReportQueryNames
{
    /// <summary>
    /// UTC 时间下界，包含。
    /// </summary>
    public const string FromUtc = "fromUtc";

    /// <summary>
    /// UTC 时间上界，不包含。
    /// </summary>
    public const string UntilUtc = "untilUtc";

    /// <summary>
    /// 可选流程编码。
    /// </summary>
    public const string FlowCode = "flowCode";

    /// <summary>
    /// 可选来源应用编码。
    /// </summary>
    public const string ClientAppCode = "clientAppCode";

    /// <summary>
    /// 可选发起部门 ID 快照。
    /// </summary>
    public const string DepartmentId = "departmentId";
}

/// <summary>
/// 报表概览；保密流程仅计入匿名数量，待办和逾期均只统计非保密流程。
/// </summary>
public sealed class AdminReportOverviewResponse
{
    /// <summary>
    /// 非保密流程的发起量。
    /// </summary>
    public int VisibleStartedCount
    {
        get; set;
    }

    /// <summary>
    /// 非保密流程的结束量。
    /// </summary>
    public int VisibleCompletedCount
    {
        get; set;
    }

    /// <summary>
    /// 非保密流程的当前待办数。
    /// </summary>
    public int VisiblePendingCount
    {
        get; set;
    }

    /// <summary>
    /// 非保密流程中已到期任务的逾期数。
    /// </summary>
    public int VisibleOverdueCount
    {
        get; set;
    }

    /// <summary>
    /// 保密流程的匿名发起量。
    /// </summary>
    public int ConfidentialStartedCount
    {
        get; set;
    }

    /// <summary>
    /// 保密流程的匿名结束量。
    /// </summary>
    public int ConfidentialCompletedCount
    {
        get; set;
    }
}

/// <summary>
/// 流程指标列表及保密流程匿名数量。
/// </summary>
public sealed class AdminReportFlowsResponse
{
    /// <summary>
    /// 非保密流程指标。
    /// </summary>
    public IReadOnlyList<AdminReportFlowItem> Items { get; set; } = [];

    /// <summary>
    /// 保密流程匿名发起量。
    /// </summary>
    public int ConfidentialStartedCount
    {
        get; set;
    }

    /// <summary>
    /// 保密流程匿名结束量。
    /// </summary>
    public int ConfidentialCompletedCount
    {
        get; set;
    }
}

/// <summary>
/// 单个非保密流程的数量和耗时。
/// </summary>
public sealed class AdminReportFlowItem
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 按发起时间计算的发起量。
    /// </summary>
    public int StartedCount
    {
        get; set;
    }

    /// <summary>
    /// 按结束时间计算的结束量。
    /// </summary>
    public int CompletedCount
    {
        get; set;
    }

    /// <summary>
    /// 已通过数量。
    /// </summary>
    public int ApprovedCount
    {
        get; set;
    }

    /// <summary>
    /// 已拒绝数量。
    /// </summary>
    public int RejectedCount
    {
        get; set;
    }

    /// <summary>
    /// 已撤回数量。
    /// </summary>
    public int WithdrawnCount
    {
        get; set;
    }

    /// <summary>
    /// 已结束审批单的平均历时分钟数，无样本时为空。
    /// </summary>
    public double? AverageDurationMinutes
    {
        get; set;
    }

    /// <summary>
    /// 已结束审批单的最近秩 P90 历时分钟数，无样本时为空。
    /// </summary>
    public double? P90DurationMinutes
    {
        get; set;
    }
}

/// <summary>
/// 非保密流程的节点指标列表。
/// </summary>
public sealed class AdminReportNodesResponse
{
    /// <summary>
    /// 节点指标。
    /// </summary>
    public IReadOnlyList<AdminReportNodeItem> Items { get; set; } = [];
}

/// <summary>
/// 单个非保密流程节点的处理与到期指标。
/// </summary>
public sealed class AdminReportNodeItem
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
    /// 时间段内完成任务数。
    /// </summary>
    public int CompletedCount
    {
        get; set;
    }

    /// <summary>
    /// 有真实处理起点的平均处理分钟数。
    /// </summary>
    public double? AverageDurationMinutes
    {
        get; set;
    }

    /// <summary>
    /// 到期时间落在时间段且观察时已到期的任务数。
    /// </summary>
    public int DueCount
    {
        get; set;
    }

    /// <summary>
    /// 已到期任务中的逾期数量。
    /// </summary>
    public int OverdueCount
    {
        get; set;
    }
}

/// <summary>
/// 非保密流程的人员指标列表。
/// </summary>
public sealed class AdminReportPeopleResponse
{
    /// <summary>
    /// 实际处理人指标。
    /// </summary>
    public IReadOnlyList<AdminReportPersonItem> Items { get; set; } = [];
}

/// <summary>
/// 单个实际处理人的非保密任务指标。
/// </summary>
public sealed class AdminReportPersonItem
{
    /// <summary>
    /// 实际处理人的内部人员 ID。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;

    /// <summary>
    /// 当前待处理任务数，不含排队。
    /// </summary>
    public int PendingCount
    {
        get; set;
    }

    /// <summary>
    /// 时间段内完成任务数。
    /// </summary>
    public int CompletedCount
    {
        get; set;
    }

    /// <summary>
    /// 有真实处理起点的平均处理分钟数。
    /// </summary>
    public double? AverageDurationMinutes
    {
        get; set;
    }

    /// <summary>
    /// 已到期任务中的逾期数量。
    /// </summary>
    public int OverdueCount
    {
        get; set;
    }
}
