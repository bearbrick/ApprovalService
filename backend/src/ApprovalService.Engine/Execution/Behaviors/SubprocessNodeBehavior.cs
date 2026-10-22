using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 子流程节点请求创建子审批单，再根据配置等待或继续。
/// </summary>
internal sealed class SubprocessNodeBehavior : INodeBehavior
{
    /// <inheritdoc />
    public string NodeType => FlowNodeTypes.Subprocess;

    /// <inheritdoc />
    public EngineNodeDecision Decide(EngineNodeInput input)
    {
        if (input.Context.SubprocessDepth >= EngineLimits.MaxSubprocessDepth)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "子流程嵌套超过上限。");
        }

        return new EngineNodeDecision
        {
            Action = input.Node.Subprocess?.WaitForCompletion == false
                ? EngineNodeAction.Move
                : EngineNodeAction.WaitForSubprocess,
            EventKind = "subprocessRequested",
            TargetCode = input.Node.Subprocess?.FlowCode,
        };
    }
}
