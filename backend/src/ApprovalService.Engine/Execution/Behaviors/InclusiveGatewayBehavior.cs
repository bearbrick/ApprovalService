using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 包容网关激活全部命中分支，并只等待这些分支汇合。
/// </summary>
internal sealed class InclusiveGatewayBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.InclusiveGateway;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input) =>
        input.Node.GatewayDirection == FlowGatewayDirections.Split
            ? new EngineNodeDecision
            {
                Action = EngineNodeAction.Split,
                SelectedEdgeIds = GatewaySelection.Select(input.Node, input.Context),
                JoinPairId = input.Node.GatewayPairId,
            }
            : new EngineNodeDecision { Action = EngineNodeAction.Join };
}
