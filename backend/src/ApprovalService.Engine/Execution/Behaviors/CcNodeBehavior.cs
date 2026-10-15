using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 抄送节点按已阅要求建立任务或直接发出送达事件。
/// </summary>
internal sealed class CcNodeBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.Cc;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input) => input.Node.Approval?.ReadReceiptRequired == true
        ? new EngineNodeDecision
        {
            Action = EngineNodeAction.CreateTask,
            TaskCompletionKind = EngineCommandKinds.Acknowledge,
        }
        : new EngineNodeDecision { Action = EngineNodeAction.Move, EventKind = "ccDelivered" };
}
