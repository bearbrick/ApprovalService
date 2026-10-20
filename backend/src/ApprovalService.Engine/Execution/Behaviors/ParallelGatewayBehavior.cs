using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 并行网关激活全部出线，并等待全部分支汇合。
/// </summary>
internal sealed class ParallelGatewayBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.ParallelGateway;

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
