using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 用同一规则解释发布图的节点动作配置与运行命令。
/// </summary>
internal static class TaskActionPermission
{
    /// <summary>
    /// 空配置不额外限制；存量办理图的 submit 仅允许办理任务的 handle 命令。
    /// </summary>
    /// <param name="configured">发布节点允许的设计动作编码。</param>
    /// <param name="nodeType">当前运行节点种类。</param>
    /// <param name="candidate">本次运行命令编码。</param>
    /// <returns>当前节点允许该运行命令。</returns>
    public static bool Permits(IReadOnlyList<string>? configured, string nodeType, string candidate) =>
        configured is not { Count: > 0 } || configured.Contains(candidate, StringComparer.Ordinal) ||
        nodeType == FlowNodeTypes.Handling && candidate == EngineCommandKinds.Handle &&
        configured.Contains(FlowActionKinds.Submit, StringComparer.Ordinal);
}
