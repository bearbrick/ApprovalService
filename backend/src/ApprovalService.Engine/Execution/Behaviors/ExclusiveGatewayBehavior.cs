using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 排他网关仅选择优先级最高的命中分支，汇合不等待未选中的分支。
/// </summary>
internal sealed class ExclusiveGatewayBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.ExclusiveGateway;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input) =>
        input.Node.GatewayDirection == FlowGatewayDirections.Split
            ? new EngineNodeDecision
            {
                Action = EngineNodeAction.Split,
                SelectedEdgeIds = GatewaySelection.Select(input.Node, input.Context),
            }
            : new EngineNodeDecision { Action = EngineNodeAction.Move };
}
