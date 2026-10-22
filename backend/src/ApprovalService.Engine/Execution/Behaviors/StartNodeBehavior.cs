using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 开始节点沿唯一出线推进。
/// </summary>
internal sealed class StartNodeBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.Start;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input) => new() { Action = EngineNodeAction.Move };
}
