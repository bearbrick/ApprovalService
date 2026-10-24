namespace ApprovalService.Contracts.Flows;

/// <summary>
/// 一个流程版本的完整自由画布定义；发布后不可修改。
/// </summary>
public sealed class FlowDefinition
{
    /// <summary>
    /// 流程定义格式版本。
    /// </summary>
    public int SchemaVersion { get; set; } = FlowSchema.CurrentVersion;

    /// <summary>
    /// 业务系统使用的流程编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 流程显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 唯一的设计方式：自由画布。
    /// </summary>
    public FlowCanvas Canvas { get; set; } = new();

    /// <summary>
    /// 业务系统提交的审批数据定义。
    /// </summary>
    public FlowDataDefinition Data { get; set; } = new();

    /// <summary>
    /// 审批详情的只读展示排版。
    /// </summary>
    public FlowDetailLayout DetailLayout { get; set; } = new();

    /// <summary>
    /// 审批单打印选项。
    /// </summary>
    public FlowPrintSettings PrintSettings { get; set; } = new();

    /// <summary>
    /// 可发起此流程的接入应用编码；空集合表示由授权策略决定。
    /// </summary>
    public List<string> AllowedClientApps { get; set; } = [];

    /// <summary>
    /// 可发起此流程的部门编码；空集合表示不额外限制部门。
    /// </summary>
    public List<string> AllowedInitiatorDepartments { get; set; } = [];

    /// <summary>
    /// 可发起此流程的角色编码；空集合表示不额外限制角色。
    /// </summary>
    public List<string> AllowedInitiatorRoles { get; set; } = [];

    /// <summary>
    /// 是否为仅参与人可见的保密流程；旧定义缺省为非保密。
    /// </summary>
    public bool IsConfidential
    {
        get; set;
    }

    /// <summary>
    /// 是否仅在首位人工处理人操作前允许发起人撤回；省略时审批结束前均可撤回。
    /// </summary>
    public bool WithdrawBeforeFirstDecision
    {
        get; set;
    }

    /// <summary>
    /// 退回后重新提交的走法：restart 或 returnNode；省略时从头开始。
    /// </summary>
    public string ReturnMode { get; set; } = FlowReturnModes.Restart;
}

/// <summary>
/// 自由画布的节点、连线及仅供设计器使用的位置。
/// </summary>
public sealed class FlowCanvas
{
    /// <summary>
    /// 画布节点。
    /// </summary>
    public List<FlowNode> Nodes { get; set; } = [];

    /// <summary>
    /// 画布连线。
    /// </summary>
    public List<FlowEdge> Edges { get; set; } = [];
}

/// <summary>
/// 自由画布节点，运行时不使用坐标。
/// </summary>
public sealed class FlowNode
{
    /// <summary>
    /// 当前流程内稳定且唯一的节点 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 节点种类，见 <see cref="FlowNodeTypes"/>。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 节点显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 画布横坐标，不参与运行语义。
    /// </summary>
    public double X
    {
        get; set;
    }

    /// <summary>
    /// 画布纵坐标，不参与运行语义。
    /// </summary>
    public double Y
    {
        get; set;
    }

    /// <summary>
    /// 分叉与汇合共享的配对 ID，仅网关使用。
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
    /// 审批、办理或抄送节点的人员来源。
    /// </summary>
    public FlowAssignee? Assignee
    {
        get; set;
    }

    /// <summary>
    /// 审批与办理规则。
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
    /// 当前节点对审批字段的可见与填写权限。
    /// </summary>
    public List<FlowFieldPermission> FieldPermissions { get; set; } = [];
}

/// <summary>
/// 自由画布上的有向连线。
/// </summary>
public sealed class FlowEdge
{
    /// <summary>
    /// 当前流程内稳定且唯一的连线 ID。
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
    /// 条件分支的选择顺序，数字越小越先尝试。
    /// </summary>
    public int Priority
    {
        get; set;
    }

    /// <summary>
    /// 无其他条件满足时走的兜底连线。
    /// </summary>
    public bool IsDefault
    {
        get; set;
    }

    /// <summary>
    /// 有限表达式，字段引用写成 ${fieldKey}；兜底线不填。
    /// </summary>
    public string? Condition
    {
        get; set;
    }
}
