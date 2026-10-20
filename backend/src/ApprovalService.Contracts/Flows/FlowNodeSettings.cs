namespace ApprovalService.Contracts.Flows;

/// <summary>
/// 从组织架构、业务数据或业务系统寻找节点处理人的配置。
/// </summary>
public sealed class FlowAssignee
{
    /// <summary>
    /// 来源编码，例如 member、role、position、grade、manager、managerChain、departmentHead、dataDepartmentHead、divisionLeader、dataPerson、matrix、businessApi、previousSelection、initiatorSelection、initiator。
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 来源需要的成员、角色、岗位、职级、矩阵或接口编码。
    /// </summary>
    public string? Value
    {
        get; set;
    }

    /// <summary>
    /// 来源引用的审批数据字段 key。
    /// </summary>
    public string? FieldKey
    {
        get; set;
    }

    /// <summary>
    /// 上级或上级部门级数。
    /// </summary>
    public int? Level
    {
        get; set;
    }
}

/// <summary>
/// 人工节点的多人审批及异常处理设置。
/// </summary>
public sealed class FlowApprovalSettings
{
    /// <summary>
    /// 多人方式：sequential、all、any、vote。
    /// </summary>
    public string Mode { get; set; } = "sequential";

    /// <summary>
    /// 票签通过比例，取值范围为 0 到 1；仅 vote 使用。
    /// </summary>
    public decimal? VoteThreshold
    {
        get; set;
    }

    /// <summary>
    /// 票签中按人员 ID 设定的权重；未列出的人员权重为 1。
    /// </summary>
    public Dictionary<string, decimal> VoteWeights { get; set; } = [];

    /// <summary>
    /// 会签有人拒绝后的处理：immediate 或 waitAll。
    /// </summary>
    public string RejectBehavior { get; set; } = "immediate";

    /// <summary>
    /// 找不到处理人时的处理：block、approve、administrator 或 specified。
    /// </summary>
    public string MissingAssigneeBehavior { get; set; } = "block";

    /// <summary>
    /// 处理人是发起人时的处理：skip、self 或 manager。
    /// </summary>
    public string SelfApprovalBehavior { get; set; } = "skip";

    /// <summary>
    /// 同一人重复出现时的处理：adjacentOnce、once 或 everyTime。
    /// </summary>
    public string DuplicateAssigneeBehavior { get; set; } = "adjacentOnce";

    /// <summary>
    /// 处理人离职时的处理：manager 或 administrator。
    /// </summary>
    public string InactiveAssigneeBehavior { get; set; } = "manager";

    /// <summary>
    /// 当前节点允许的操作编码，如 approve、reject、return、transfer、addSigner。
    /// </summary>
    public List<string> AllowedActions { get; set; } = [];

    /// <summary>
    /// 审批意见是否必填。
    /// </summary>
    public bool CommentRequired
    {
        get; set;
    }

    /// <summary>
    /// 抄送节点是否需要确认已阅。
    /// </summary>
    public bool ReadReceiptRequired
    {
        get; set;
    }

    /// <summary>
    /// 到期前及超时提醒的时限，单位分钟；空值表示不启用。
    /// </summary>
    public int? DueMinutes
    {
        get; set;
    }
}

/// <summary>
/// 子流程的调用及数据传递设置。
/// </summary>
public sealed class FlowSubprocessSettings
{
    /// <summary>
    /// 要调用的流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 是否等待子流程结束后继续。
    /// </summary>
    public bool WaitForCompletion { get; set; } = true;

    /// <summary>
    /// 子流程字段 key 到当前流程字段 key 的映射。
    /// </summary>
    public Dictionary<string, string> DataMappings { get; set; } = [];
}

/// <summary>
/// 自动节点向业务系统发出的动作设置。
/// </summary>
public sealed class FlowAutomationSettings
{
    /// <summary>
    /// 接入应用配置的动作编码，由出站适配层执行。
    /// </summary>
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>
    /// 失败耗尽重试后是否交管理员处理。
    /// </summary>
    public bool EscalateToAdministrator { get; set; } = true;
}

/// <summary>
/// 当前节点对指定字段的展示或填写权限。
/// </summary>
public sealed class FlowFieldPermission
{
    /// <summary>
    /// 字段 key，明细子字段写成 tableKey.childKey。
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>
    /// 访问级别，见 <see cref="FlowFieldAccess"/>。
    /// </summary>
    public string Access { get; set; } = FlowFieldAccess.ReadOnly;
}
