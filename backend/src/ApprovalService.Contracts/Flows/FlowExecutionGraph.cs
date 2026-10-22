namespace ApprovalService.Contracts.Flows;

/// <summary>
/// 自由画布发布后编译出的运行语义图，不包含设计坐标。
/// </summary>
public sealed class FlowExecutionGraph
{
    /// <summary>
    /// 与设计定义一致的格式版本。
    /// </summary>
    public int SchemaVersion { get; set; } = FlowSchema.CurrentVersion;

    /// <summary>
    /// 发布时固定的撤回限制；省略时审批结束前均可撤回。
    /// </summary>
    public bool WithdrawBeforeFirstDecision
    {
        get; set;
    }

    /// <summary>
    /// 发布时固定的退回后重提交走法；省略时从头开始。
    /// </summary>
    public string ReturnMode { get; set; } = FlowReturnModes.Restart;

    /// <summary>
    /// 可执行的节点。
    /// </summary>
    public List<FlowExecutionNode> Nodes { get; set; } = [];

    /// <summary>
    /// 可执行的有向连线。
    /// </summary>
    public List<FlowExecutionEdge> Edges { get; set; } = [];
}

/// <summary>
/// 一个运行节点，保留规则设置但不携带画布位置。
/// </summary>
public sealed class FlowExecutionNode
{
    /// <summary>
    /// 设计定义里的稳定节点 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 节点种类。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 节点名称，供操作记录展示。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 成对网关共享的 ID。
    /// </summary>
    public string? GatewayPairId
    {
        get; set;
    }

    /// <summary>
    /// 网关方向：split 或 join。
    /// </summary>
    public string? GatewayDirection
    {
        get; set;
    }

    /// <summary>
    /// 人工节点的人员来源。
    /// </summary>
    public FlowAssignee? Assignee
    {
        get; set;
    }

    /// <summary>
    /// 人工节点的审批与异常处理设置。
    /// </summary>
    public FlowApprovalSettings? Approval
    {
        get; set;
    }

    /// <summary>
    /// 子流程设置。
    /// </summary>
    public FlowSubprocessSettings? Subprocess
    {
        get; set;
    }

    /// <summary>
    /// 自动节点设置。
    /// </summary>
    public FlowAutomationSettings? Automation
    {
        get; set;
    }

    /// <summary>
    /// 当前节点的字段权限。
    /// </summary>
    public List<FlowFieldPermission> FieldPermissions { get; set; } = [];
}

/// <summary>
/// 一个运行连线，保留分支条件与优先级。
/// </summary>
public sealed class FlowExecutionEdge
{
    /// <summary>
    /// 设计定义里的稳定连线 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 源节点 ID。
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// 目标节点 ID。
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// 分支条件选择顺序。
    /// </summary>
    public int Priority
    {
        get; set;
    }

    /// <summary>
    /// 是否为兜底连线。
    /// </summary>
    public bool IsDefault
    {
        get; set;
    }

    /// <summary>
    /// 使用 ${fieldKey} 引用审批字段的有限表达式。
    /// </summary>
    public string? Condition
    {
        get; set;
    }
}
