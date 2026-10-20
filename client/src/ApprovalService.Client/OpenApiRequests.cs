namespace ApprovalService.Client;

/// <summary>
/// 业务系统发起审批时提交的单号、人员和数据快照。
/// </summary>
public sealed class StartApprovalRequest
{
    /// <summary>
    /// 已授权的流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务系统自己的单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 审批标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 本应用已绑定的发起人员 ID。
    /// </summary>
    public string Initiator { get; set; } = string.Empty;

    /// <summary>
    /// 可选的本应用已绑定发起部门 ID。
    /// </summary>
    public string? InitiatingDepartment
    {
        get; set;
    }

    /// <summary>
    /// 可选的本应用已绑定代发人员 ID。
    /// </summary>
    public string? SubmittedBy
    {
        get; set;
    }

    /// <summary>
    /// 业务审批数据快照；当前开放发起要求传入数据。
    /// </summary>
    public Dictionary<string, object?>? Data
    {
        get; set;
    }

    /// <summary>
    /// 业务系统原单详情地址。
    /// </summary>
    public string? DetailUrl
    {
        get; set;
    }

    /// <summary>
    /// 是否加急。
    /// </summary>
    public bool IsUrgent
    {
        get; set;
    }

    /// <summary>
    /// 自选节点 ID 到本应用已绑定人员 ID 的映射。
    /// </summary>
    public Dictionary<string, List<string>> SelectedAssignees { get; set; } = new();
}

/// <summary>
/// 人工任务的审批或协作命令。
/// </summary>
public sealed class ApprovalTaskActionRequest
{
    /// <summary>
    /// 本应用已绑定的实际操作人员 ID。
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// 命令编码，如 approve、reject、return 或 transfer。
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 审批意见或评论。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }

    /// <summary>
    /// 目标业务人员 ID。
    /// </summary>
    public string? TargetUser
    {
        get; set;
    }

    /// <summary>
    /// 退回等操作的目标节点 ID。
    /// </summary>
    public string? TargetNodeId
    {
        get; set;
    }

    /// <summary>
    /// 征询或加签关联 ID。
    /// </summary>
    public string? RelatedId
    {
        get; set;
    }

    /// <summary>
    /// 退回或加签位置等操作模式。
    /// </summary>
    public string? Mode
    {
        get; set;
    }

    /// <summary>
    /// 被提及的本应用业务人员 ID。
    /// </summary>
    public List<string> MentionedUsers { get; set; } = new();

    /// <summary>
    /// 本节点允许审批人填写的顶层字段值；不修改业务数据快照。
    /// </summary>
    public Dictionary<string, object?>? FieldValues
    {
        get; set;
    }
}
