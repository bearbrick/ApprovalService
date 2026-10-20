using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 审批操作的稳定命令编码。
/// </summary>
public static class OperationKinds
{
    /// <summary>
    /// 待办列表中的批量同意操作。
    /// </summary>
    public const string BulkApprove = "bulkApprove";
    /// <summary>
    /// 向业务系统发出另起一笔撤销审批的请求。
    /// </summary>
    public const string RequestCancellation = "requestCancellation";
    /// <summary>
    /// 将已在手的待办按有效委托一并交给代理人。
    /// </summary>
    public const string DelegateExisting = "delegateExisting";
    /// <summary>
    /// 在当前审批人之前、之后或同时加签。
    /// </summary>
    public const string AddSigner = "addSigner";
    /// <summary>
    /// 撤销尚未处理的加签任务。
    /// </summary>
    public const string RemoveSigner = "removeSigner";
    /// <summary>
    /// 下一步尚未处理前收回自己的同意。
    /// </summary>
    public const string RevokeApproval = "revokeApproval";
    /// <summary>
    /// 退回。
    /// </summary>
    public const string Return = "return";
    /// <summary>
    /// 重新提交。
    /// </summary>
    public const string Resubmit = "resubmit";
    /// <summary>
    /// 转交。
    /// </summary>
    public const string Transfer = "transfer";
    /// <summary>
    /// 催办。
    /// </summary>
    public const string Remind = "remind";
    /// <summary>
    /// 管理员修改当前审批人。
    /// </summary>
    public const string Reassign = "reassign";
    /// <summary>
    /// 管理员跳转节点。
    /// </summary>
    public const string Jump = "jump";
    /// <summary>
    /// 管理员离职交接。
    /// </summary>
    public const string Handover = "handover";
    /// <summary>
    /// 组织来源确认人员停用后，按当前节点规则重分配一项在途任务。
    /// </summary>
    public const string ReassignInactive = "reassignInactive";
    /// <summary>
    /// 管理员重发通知或回调。
    /// </summary>
    public const string Resend = "resend";
    /// <summary>
    /// 评论并可提及人员。
    /// </summary>
    public const string Comment = "comment";
    /// <summary>
    /// 暂存意见。
    /// </summary>
    public const string SaveDraft = "saveDraft";
    /// <summary>
    /// 抄送。
    /// </summary>
    public const string CopyTo = "copyTo";
    /// <summary>
    /// 征询意见。
    /// </summary>
    public const string Consult = "consult";
    /// <summary>
    /// 回复征询。
    /// </summary>
    public const string Reply = "reply";
    /// <summary>
    /// 要求发起人补充。
    /// </summary>
    public const string RequestSupplement = "requestSupplement";
    /// <summary>
    /// 提交补充。
    /// </summary>
    public const string Supplement = "supplement";
}

/// <summary>
/// 单次命令携带的身份和用户输入。
/// </summary>
public sealed class OperationInput
{
    /// <summary>
    /// 实际操作人的 ID。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// 审批意见或评论。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }
    /// <summary>
    /// 管理员干预的原因。
    /// </summary>
    public string? Reason
    {
        get; set;
    }
    /// <summary>
    /// 目标人员 ID。
    /// </summary>
    public string? TargetUserId
    {
        get; set;
    }
    /// <summary>
    /// 目标节点 ID。
    /// </summary>
    public string? TargetNodeId
    {
        get; set;
    }
    /// <summary>
    /// 关联征询或任务 ID。
    /// </summary>
    public string? RelatedId
    {
        get; set;
    }
    /// <summary>
    /// 退回后重新提交方式：restart 或 returnNode。
    /// </summary>
    public string? Mode
    {
        get; set;
    }
    /// <summary>
    /// 被提及的人员。
    /// </summary>
    public List<string> MentionedUserIds { get; set; } = [];
    /// <summary>
    /// 本次是否由应用层确认管理员权限。
    /// </summary>
    public bool IsAdministrator
    {
        get; set;
    }
    /// <summary>
    /// 本次同意来自批量操作。
    /// </summary>
    public bool IsBulkApproval
    {
        get; set;
    }
}

/// <summary>
/// 外部系统在单次引擎调用前固定提供的操作规则与委托快照。
/// </summary>
public sealed class OperationContext
{
    /// <summary>
    /// 审批单发起人 ID。
    /// </summary>
    public string InitiatorId { get; set; } = string.Empty;
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;
    /// <summary>
    /// 是否只允许首位审批人处理前撤回。
    /// </summary>
    public bool WithdrawBeforeFirstDecision
    {
        get; set;
    }
    /// <summary>
    /// 发布流程的退回后重新提交方式：restart 或 returnNode。
    /// </summary>
    public string ReturnMode { get; set; } = "restart";
    /// <summary>
    /// 外部撤销审批已经通过，允许将原审批单标记为已撤销。
    /// </summary>
    public bool CancellationApproved
    {
        get; set;
    }
    /// <summary>
    /// 在调用时间有效性判断前的委托配置快照。
    /// </summary>
    public List<OperationDelegation> Delegations { get; set; } = [];
}

/// <summary>
/// 一项委托设置，由应用层负责持久化及范围授权。
/// </summary>
public sealed class OperationDelegation
{
    /// <summary>
    /// 委托人 ID。
    /// </summary>
    public string PrincipalId { get; set; } = string.Empty;
    /// <summary>
    /// 代理人 ID。
    /// </summary>
    public string DelegateId { get; set; } = string.Empty;
    /// <summary>
    /// 有效期开始，含边界。
    /// </summary>
    public DateTimeOffset StartsAt
    {
        get; set;
    }
    /// <summary>
    /// 有效期结束，不含边界。
    /// </summary>
    public DateTimeOffset EndsAt
    {
        get; set;
    }
    /// <summary>
    /// 流程编码；空集合代表全部流程。
    /// </summary>
    public List<string> FlowCodes { get; set; } = [];
}

/// <summary>
/// 事件里可还原的实际操作人、代办关系及内容。
/// </summary>
public sealed class OperationAudit
{
    /// <summary>
    /// 实际操作人 ID。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// 被代理的原审批人 ID。
    /// </summary>
    public string? OnBehalfOfId
    {
        get; set;
    }
    /// <summary>
    /// 审批意见。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }
    /// <summary>
    /// 管理员干预原因。
    /// </summary>
    public string? Reason
    {
        get; set;
    }
    /// <summary>
    /// 关联目标人员。
    /// </summary>
    public string? TargetUserId
    {
        get; set;
    }
    /// <summary>
    /// 被提及的人员。
    /// </summary>
    public List<string> MentionedUserIds { get; set; } = [];
}

/// <summary>
/// 审批操作附加状态，随运行状态在同一事务中持久化。
/// </summary>
public sealed class OperationState
{
    /// <summary>
    /// 发起人 ID，开始命令时固定。
    /// </summary>
    public string InitiatorId { get; set; } = string.Empty;
    /// <summary>
    /// 流程编码，开始命令时固定。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;
    /// <summary>
    /// 发起时固定的首人处理后禁止撤回设置。
    /// </summary>
    public bool WithdrawBeforeFirstDecision
    {
        get; set;
    }
    /// <summary>
    /// 发起时固定的重新提交走法。
    /// </summary>
    public string ReturnMode { get; set; } = "restart";
    /// <summary>
    /// 是否已经为原审批单申请另起撤销审批。
    /// </summary>
    public bool CancellationRequested
    {
        get; set;
    }
    /// <summary>
    /// 已有人工任务完成。
    /// </summary>
    public bool HasHumanDecision
    {
        get; set;
    }
    /// <summary>
    /// 已作出的人工作业决定次数，用于判断下一步是否已经处理。
    /// </summary>
    public int HumanDecisionCount
    {
        get; set;
    }
    /// <summary>
    /// 最近一次同意前的可恢复状态。
    /// </summary>
    public OperationUndoFrame? LastApprovalUndo
    {
        get; set;
    }
    /// <summary>
    /// 最近的退回或补充请求。
    /// </summary>
    public OperationPause? Pause
    {
        get; set;
    }
    /// <summary>
    /// 催办时间按在途节点记录。
    /// </summary>
    public Dictionary<string, DateTimeOffset> LastReminders { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 各任务暂存的意见。
    /// </summary>
    public Dictionary<string, string> DraftOpinions { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 管理员对尚未到达节点设置的审批人覆盖值。
    /// </summary>
    public Dictionary<string, string> AssigneeOverrides { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 审批人临时抄送过的可见人员。
    /// </summary>
    public HashSet<string> CopyRecipients { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 尚未回复的征询。
    /// </summary>
    public List<OperationConsultation> Consultations { get; set; } = [];
    /// <summary>
    /// 经过的人工节点，供退回目标校验。
    /// </summary>
    public List<OperationVisit> Visits { get; set; } = [];
}

/// <summary>
/// 退回或补充时恢复审批所需的信息。
/// </summary>
public sealed class OperationPause
{
    /// <summary>
    /// 暂停类别。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>
    /// 退回发生的节点。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    /// <summary>
    /// 原令牌 ID。
    /// </summary>
    public string TokenId { get; set; } = string.Empty;
    /// <summary>
    /// 重新提交方式。
    /// </summary>
    public string Mode { get; set; } = string.Empty;
    /// <summary>
    /// 原令牌分叉路径。
    /// </summary>
    public List<EngineForkFrame> Forks { get; set; } = [];
}

/// <summary>
/// 一次征询及回复。
/// </summary>
public sealed class OperationConsultation
{
    /// <summary>
    /// 征询 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 发问任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>
    /// 受询人 ID。
    /// </summary>
    public string RecipientId { get; set; } = string.Empty;
    /// <summary>
    /// 是否已回复。
    /// </summary>
    public bool Replied
    {
        get; set;
    }
}

/// <summary>
/// 曾经进入的人工节点及其并行分支路径。
/// </summary>
public sealed class OperationVisit
{
    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    /// <summary>
    /// 进入时的分叉路径。
    /// </summary>
    public List<EngineForkFrame> Forks { get; set; } = [];
}

/// <summary>
/// 撤销同意的固定前态及同意者身份。
/// </summary>
public sealed class OperationUndoFrame
{
    /// <summary>
    /// 同意命令之前的状态快照，不携带更早的撤销帧。
    /// </summary>
    public EngineState Snapshot { get; set; } = new();
    /// <summary>
    /// 允许收回同意的实际操作人。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// 本次同意任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>
    /// 同意后的累计人工作业次数。
    /// </summary>
    public int DecisionCountAfter
    {
        get; set;
    }
    /// <summary>
    /// 同意命令完成后的已处理请求数，防止撤销时抹掉后续已留痕操作。
    /// </summary>
    public int ProcessedRequestCountAfter
    {
        get; set;
    }
}
