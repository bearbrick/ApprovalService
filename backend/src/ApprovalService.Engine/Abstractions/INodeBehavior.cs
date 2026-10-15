using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Abstractions;

/// <summary>
/// 纯节点行为扩展点。实现只根据输入快照做决定，不修改输入或调用外部系统。
/// </summary>
public interface INodeBehavior
{
    /// <summary>
    /// 负责的节点类型编码。
    /// </summary>
    string NodeType
    {
        get;
    }

    /// <summary>
    /// 决定令牌在节点上的下一步动作。
    /// </summary>
    /// <param name="input">执行图、状态和外部求值结果的只读约定快照。</param>
    /// <returns>由运行时统一执行的节点决定。</returns>
    EngineNodeDecision Decide(EngineNodeInput input);
}

/// <summary>
/// 节点行为可返回的有限动作；实际修改状态只由运行时完成。
/// </summary>
public enum EngineNodeAction
{
    /// <summary>
    /// 沿唯一出线推进。
    /// </summary>
    Move,

    /// <summary>
    /// 到达结束节点。
    /// </summary>
    Complete,

    /// <summary>
    /// 建立单人任务。
    /// </summary>
    CreateTask,

    /// <summary>
    /// 按选中连线分叉。
    /// </summary>
    Split,

    /// <summary>
    /// 等待成对网关汇合。
    /// </summary>
    Join,

    /// <summary>
    /// 等待自动动作结果命令。
    /// </summary>
    WaitForAction,

    /// <summary>
    /// 等待子流程结果命令。
    /// </summary>
    WaitForSubprocess,
}

/// <summary>
/// 行为实现所需的输入。节点行为不得修改其中引用的状态、令牌或执行图。
/// </summary>
public sealed class EngineNodeInput
{
    /// <summary>
    /// 当前节点。
    /// </summary>
    public FlowExecutionNode Node { get; init; } = new();

    /// <summary>
    /// 当前令牌。
    /// </summary>
    public EngineToken Token { get; init; } = new();

    /// <summary>
    /// 当前审批单状态。
    /// </summary>
    public EngineState State { get; init; } = new();

    /// <summary>
    /// 调用方提供的固定上下文。
    /// </summary>
    public EngineContext Context { get; init; } = new();
}

/// <summary>
/// 节点行为的纯决定。事件和状态由运行时统一生成与更新。
/// </summary>
public sealed class EngineNodeDecision
{
    /// <summary>
    /// 运行时要执行的动作。
    /// </summary>
    public EngineNodeAction Action
    {
        get; init;
    }

    /// <summary>
    /// 分叉实际激活的连线 ID，按优先级排列。
    /// </summary>
    public IReadOnlyList<string> SelectedEdgeIds { get; init; } = [];

    /// <summary>
    /// 需要在汇合时等待的成对网关 ID；为空表示不建立分叉栈。
    /// </summary>
    public string? JoinPairId
    {
        get; init;
    }

    /// <summary>
    /// 可选的附加事件种类，例如自动动作待执行。
    /// </summary>
    public string? EventKind
    {
        get; init;
    }

    /// <summary>
    /// 附加事件的动作或子流程编码。
    /// </summary>
    public string? TargetCode
    {
        get; init;
    }
}
