using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Policies;

namespace ApprovalService.Engine.Execution;

/// <summary>
/// 审批单运行状态的稳定编码。
/// </summary>
public static class EngineInstanceStatuses
{
    /// <summary>
    /// 审批中。
    /// </summary>
    public const string Running = "running";
    /// <summary>
    /// 已通过。
    /// </summary>
    public const string Approved = "approved";
    /// <summary>
    /// 已拒绝。
    /// </summary>
    public const string Rejected = "rejected";
    /// <summary>
    /// 已撤回。
    /// </summary>
    public const string Withdrawn = "withdrawn";
    /// <summary>
    /// 已终止。
    /// </summary>
    public const string Terminated = "terminated";
    /// <summary>
    /// 等待发起人处理。
    /// </summary>
    public const string AwaitingInitiator = "awaitingInitiator";
    /// <summary>
    /// 已撤销。
    /// </summary>
    public const string Cancelled = "cancelled";
    /// <summary>
    /// 执行异常，等待管理员处理。
    /// </summary>
    public const string Faulted = "faulted";
}

/// <summary>
/// 人工任务状态的稳定编码。
/// </summary>
public static class EngineTaskStatuses
{
    /// <summary>
    /// 依次审批中等待前一人处理。
    /// </summary>
    public const string Queued = "queued";
    /// <summary>
    /// 待处理。
    /// </summary>
    public const string Pending = "pending";
    /// <summary>
    /// 已同意。
    /// </summary>
    public const string Approved = "approved";
    /// <summary>
    /// 已拒绝。
    /// </summary>
    public const string Rejected = "rejected";
    /// <summary>
    /// 已退回。
    /// </summary>
    public const string Returned = "returned";
    /// <summary>
    /// 已转交。
    /// </summary>
    public const string Transferred = "transferred";
    /// <summary>
    /// 已取消。
    /// </summary>
    public const string Cancelled = "cancelled";
}

/// <summary>
/// 运行命令种类；所有状态变更均从统一入口进入。
/// </summary>
public static class EngineCommandKinds
{
    /// <summary>
    /// 开始。
    /// </summary>
    public const string Start = "start";
    /// <summary>
    /// 同意审批。
    /// </summary>
    public const string Approve = "approve";
    /// <summary>
    /// 完成办理。
    /// </summary>
    public const string Handle = "handle";
    /// <summary>
    /// 确认已阅。
    /// </summary>
    public const string Acknowledge = "acknowledge";
    /// <summary>
    /// 拒绝。
    /// </summary>
    public const string Reject = "reject";
    /// <summary>
    /// 自动动作成功。
    /// </summary>
    public const string ActionSucceeded = "actionSucceeded";
    /// <summary>
    /// 自动动作失败。
    /// </summary>
    public const string ActionFailed = "actionFailed";
    /// <summary>
    /// 子流程完成。
    /// </summary>
    public const string SubprocessCompleted = "subprocessCompleted";
    /// <summary>
    /// 撤回。
    /// </summary>
    public const string Withdraw = "withdraw";
    /// <summary>
    /// 终止。
    /// </summary>
    public const string Terminate = "terminate";
    /// <summary>
    /// 撤销。
    /// </summary>
    public const string Cancel = "cancel";
}

/// <summary>
/// 引擎命令。请求 ID 在同一审批单内唯一，也是派生事件与令牌 ID 的种子。
/// </summary>
public sealed class EngineCommand
{
    /// <summary>
    /// 命令种类。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>
    /// 调用方生成的请求 ID。
    /// </summary>
    public string RequestId { get; set; } = string.Empty;
    /// <summary>
    /// 审批单 ID，开始命令必须提供。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
    /// <summary>
    /// 人工任务 ID。
    /// </summary>
    public string? TaskId
    {
        get; set;
    }
    /// <summary>
    /// 自动节点或子流程的等待令牌 ID。
    /// </summary>
    public string? TokenId
    {
        get; set;
    }
    /// <summary>
    /// 动作或子流程结果是否成功。
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// 本次操作的身份、意见和目标参数；旧自动命令可为空。
    /// </summary>
    public OperationInput? Operation
    {
        get; set;
    }
}

/// <summary>
/// 一次命令的外部输入快照，不包含任何可触发 IO 的服务。
/// </summary>
public sealed class EngineContext
{
    /// <summary>
    /// 已发布的执行图。
    /// </summary>
    public FlowExecutionGraph Graph { get; set; } = new();
    /// <summary>
    /// 调用方提供的发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
    /// <summary>
    /// 条件表达式的已求值结果；键是连线 ID。
    /// </summary>
    public IReadOnlyDictionary<string, bool> Conditions { get; set; } = new Dictionary<string, bool>();
    /// <summary>
    /// 人工节点的已解析单人处理人；多人规则在后续任务接入。
    /// </summary>
    public IReadOnlyDictionary<string, string> Assignees { get; set; } = new Dictionary<string, string>();
    /// <summary>
    /// 当前命令到达节点时的组织、业务数据和外部取人结果快照。
    /// </summary>
    public AssigneeSnapshot? AssigneeSnapshot
    {
        get; set;
    }
    /// <summary>
    /// 发起时预览的节点人员，用于记录运行时取人变化。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PreviewAssignees
    {
        get; set;
    } =
        new Dictionary<string, IReadOnlyList<string>>();
    /// <summary>
    /// 本次额外注册的纯审批人来源。
    /// </summary>
    public IReadOnlyList<IAssigneeResolver> AssigneeResolvers { get; set; } = [];
    /// <summary>
    /// 本次额外注册的纯多人策略。
    /// </summary>
    public IReadOnlyList<IApprovalPolicy> ApprovalPolicies { get; set; } = [];
    /// <summary>
    /// 当前流程在子流程链中的层级，顶层为零。
    /// </summary>
    public int SubprocessDepth
    {
        get; set;
    }

    /// <summary>
    /// 本次执行额外注册的纯节点行为；相同类型最多一个，可覆盖内置行为。
    /// </summary>
    public IReadOnlyList<INodeBehavior> NodeBehaviors { get; set; } = [];

    /// <summary>
    /// 发起人及流程编码等由应用层提供的操作上下文。
    /// </summary>
    public OperationContext Operations { get; set; } = new();
}

/// <summary>
/// 一个分叉的激活记录。汇合只等待本次实际选中的边。
/// </summary>
public sealed class EngineForkFrame
{
    /// <summary>
    /// 本次分叉 ID。
    /// </summary>
    public string ForkId { get; set; } = string.Empty;
    /// <summary>
    /// 分叉和汇合的配对 ID。
    /// </summary>
    public string PairId { get; set; } = string.Empty;
    /// <summary>
    /// 本令牌代表的分支连线。
    /// </summary>
    public string BranchEdgeId { get; set; } = string.Empty;
    /// <summary>
    /// 本次实际选中的所有分支连线。
    /// </summary>
    public List<string> ExpectedEdgeIds { get; set; } = [];
}

/// <summary>
/// 位于节点上的执行令牌。
/// </summary>
public sealed class EngineToken
{
    /// <summary>
    /// 令牌 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 当前节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    /// <summary>
    /// 进入当前节点的连线 ID。
    /// </summary>
    public string? IncomingEdgeId
    {
        get; set;
    }
    /// <summary>
    /// 等待的外部结果：task、action 或 subprocess；空表示可立即推进。
    /// </summary>
    public string? WaitingFor
    {
        get; set;
    }
    /// <summary>
    /// 嵌套分叉栈，最后一项是最近的分叉。
    /// </summary>
    public List<EngineForkFrame> Forks { get; set; } = [];
}

/// <summary>
/// 一项人工任务。
/// </summary>
public sealed class EngineTask
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 所在令牌 ID。
    /// </summary>
    public string TokenId { get; set; } = string.Empty;
    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    /// <summary>
    /// 处理人 ID。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;
    /// <summary>
    /// 任务状态。
    /// </summary>
    public string Status { get; set; } = EngineTaskStatuses.Pending;

    /// <summary>
    /// 此任务创建时固定的完成命令。
    /// </summary>
    public string CompletionKind { get; set; } = string.Empty;

    /// <summary>
    /// 此任务是否允许拒绝。
    /// </summary>
    public bool CanReject
    {
        get; set;
    }

    /// <summary>
    /// 代理任务的原审批人；空值表示本人任务。
    /// </summary>
    public string? DelegatorId
    {
        get; set;
    }

    /// <summary>
    /// 动态加签时发起加签的任务 ID；普通任务为空。
    /// </summary>
    public string? AddedByTaskId
    {
        get; set;
    }

    /// <summary>
    /// 加签位置：before、after 或 parallel。
    /// </summary>
    public string? SignerMode
    {
        get; set;
    }
}

/// <summary>
/// 一个审批单在引擎中的完整、可持久化状态。
/// </summary>
public sealed class EngineState
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
    /// <summary>
    /// 审批单状态。
    /// </summary>
    public string Status { get; set; } = EngineInstanceStatuses.Running;
    /// <summary>
    /// 累计进入节点的步数。
    /// </summary>
    public int StepCount
    {
        get; set;
    }
    /// <summary>
    /// 未完成的令牌。
    /// </summary>
    public List<EngineToken> Tokens { get; set; } = [];
    /// <summary>
    /// 本审批单的任务。
    /// </summary>
    public List<EngineTask> Tasks { get; set; } = [];
    /// <summary>
    /// 已处理的请求 ID；应用层应与状态同事务持久化。
    /// </summary>
    public HashSet<string> ProcessedRequestIds { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 全流程已同意的人员，用于只审一次设置。
    /// </summary>
    public HashSet<string> ApprovedAssigneeIds { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 最近一名已同意的审批人，用于相邻只审一次设置。
    /// </summary>
    public string? LastApprovedAssigneeId
    {
        get; set;
    }

    /// <summary>
    /// 审批操作所需的可持久化补充状态。
    /// </summary>
    public OperationState Operations { get; set; } = new();
}

