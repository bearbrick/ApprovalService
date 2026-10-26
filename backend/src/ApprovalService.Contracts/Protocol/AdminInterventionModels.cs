namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 管理员运行干预的人员 JWT 路由。
/// </summary>
public static class AdminInterventionRoutes
{
    /// <summary>
    /// 最近在途审批摘要。
    /// </summary>
    public const string Instances = "/api/v1/admin/interventions";
    /// <summary>
    /// 已授权审批运行详情。
    /// </summary>
    public const string Instance = Instances + "/{id}";
    /// <summary>
    /// 原因必填的统一命令入口。
    /// </summary>
    public const string Commands = Instance + "/commands";
}

/// <summary>
/// 不含业务数据的运行干预摘要和选项。
/// </summary>
public sealed class AdminInterventionResponse
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 审批标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 审批编号。
    /// </summary>
    public string SerialNo { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 运行状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 当前运行修订。
    /// </summary>
    public long Version
    {
        get; set;
    }

    /// <summary>
    /// 始终锁定的发布版本。
    /// </summary>
    public string FlowVersionId { get; set; } = string.Empty;

    /// <summary>
    /// 当前待办及排队任务。
    /// </summary>
    public IReadOnlyList<AdminInterventionTask> Tasks { get; set; } = [];

    /// <summary>
    /// 锁定版本中可跳转的非汇合人工节点。
    /// </summary>
    public IReadOnlyList<AdminInterventionNode> Nodes { get; set; } = [];

}

/// <summary>
/// 可改派的当前任务。
/// </summary>
public sealed class AdminInterventionTask
{
    /// <summary>
    /// 不透明任务 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 所属节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 节点名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 当前内部人员 ID。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;

    /// <summary>
    /// 待办或排队状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

}

/// <summary>
/// 可跳转的人工节点。
/// </summary>
public sealed class AdminInterventionNode
{
    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 节点名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

}

/// <summary>
/// 一次明确确认的管理员干预命令，不接收调用方自报权限。
/// </summary>
public sealed class AdminInterventionRequest
{
    /// <summary>
    /// 改派 reassign、跳转 jump 或终止 terminate。
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 详情读取时的运行修订。
    /// </summary>
    public long ExpectedVersion
    {
        get; set;
    }

    /// <summary>
    /// 必填原因，长度二至二百字。
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 改派目标任务 ID。
    /// </summary>
    public string? TaskId
    {
        get; set;
    }

    /// <summary>
    /// 改派目标内部人员 ID。
    /// </summary>
    public string? TargetPersonId
    {
        get; set;
    }

    /// <summary>
    /// 跳转目标人工节点 ID。
    /// </summary>
    public string? TargetNodeId
    {
        get; set;
    }


}
