using ApprovalService.Contracts.Flows;

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
    /// 当前流程在子流程链中的层级，顶层为零。
    /// </summary>
    public int SubprocessDepth
    {
        get; set;
    }
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
}

/// <summary>
/// 引擎产生的一条只追加事件，应用层负责持久化及出站派发。
/// </summary>
public sealed class EngineEvent
{
    /// <summary>
    /// 确定性事件 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 事件种类。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>
    /// 发生时间，由调用方提供。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
    /// <summary>
    /// 相关节点 ID。
    /// </summary>
    public string? NodeId
    {
        get; set;
    }
    /// <summary>
    /// 相关令牌 ID。
    /// </summary>
    public string? TokenId
    {
        get; set;
    }
    /// <summary>
    /// 相关任务 ID。
    /// </summary>
    public string? TaskId
    {
        get; set;
    }
    /// <summary>
    /// 子流程编码或自动动作编码。
    /// </summary>
    public string? TargetCode
    {
        get; set;
    }
}

/// <summary>
/// 一次命令的纯计算结果。
/// </summary>
public sealed class EngineResult
{
    /// <summary>
    /// 新状态。
    /// </summary>
    public EngineState State { get; set; } = new();
    /// <summary>
    /// 本命令产生的新事件；幂等重放为空。
    /// </summary>
    public List<EngineEvent> Events { get; set; } = [];
    /// <summary>
    /// 同一请求 ID 已处理过时为真。
    /// </summary>
    public bool IsReplay
    {
        get; set;
    }
}

/// <summary>
/// 可由调用方纠正的无效命令或运行图。
/// </summary>
public sealed class EngineException : Exception
{
    /// <summary>
    /// 构造带稳定错误码和中文说明的错误。
    /// </summary>
    /// <param name="code">错误码。</param>
    /// <param name="message">中文说明。</param>
    public EngineException(string code, string message) : base(message) => Code = code;

    /// <summary>
    /// 错误码。
    /// </summary>
    public string Code
    {
        get;
    }
}
