using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 结束节点消费令牌。
/// </summary>
internal sealed class EndNodeBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.End;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input) => new() { Action = EngineNodeAction.Complete };
}
