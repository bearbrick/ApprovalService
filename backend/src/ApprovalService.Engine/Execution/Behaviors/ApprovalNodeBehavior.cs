using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 审批节点生成待处理任务。
/// </summary>
internal sealed class ApprovalNodeBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.Approval;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input) => new() { Action = EngineNodeAction.CreateTask };
}
