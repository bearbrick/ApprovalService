using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 自动节点只请求外部动作，等待结果命令。
/// </summary>
internal sealed class AutomaticNodeBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.Automatic;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input) => new()
    {
        Action = EngineNodeAction.WaitForAction,
        EventKind = "actionPending",
        TargetCode = input.Node.Automation?.ActionCode,
    };
}
