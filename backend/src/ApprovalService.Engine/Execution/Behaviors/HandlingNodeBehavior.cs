using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 办理节点生成待处理任务。
/// </summary>
internal sealed class HandlingNodeBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.Handling;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input) => new() { Action = EngineNodeAction.CreateTask };
}
