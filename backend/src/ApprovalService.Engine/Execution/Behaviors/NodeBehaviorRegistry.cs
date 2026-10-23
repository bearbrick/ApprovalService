using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 内置节点行为登记处；扩展节点可通过执行上下文直接注册，不需修改运行时推进器。
/// </summary>
internal static class NodeBehaviorRegistry
{
    /// <summary>
    /// 内置行为按节点类型索引。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, INodeBehavior> BuiltIns = new INodeBehavior[]
    {
        new StartNodeBehavior(),
        new EndNodeBehavior(),
        new ApprovalNodeBehavior(),
        new HandlingNodeBehavior(),
        new CcNodeBehavior(),
        new ExclusiveGatewayBehavior(),
        new InclusiveGatewayBehavior(),
        new ParallelGatewayBehavior(),
        new SubprocessNodeBehavior(),
        new AutomaticNodeBehavior(),
    }.ToDictionary(item => item.NodeType, StringComparer.Ordinal);

    /// <summary>
    /// 查找本次上下文或内置行为；拒绝歧义和未注册类型。
    /// </summary>
    /// <param name="nodeType">节点类型。</param>
    /// <param name="context">本次上下文。</param>
    /// <returns>唯一的节点行为。</returns>
    public static INodeBehavior Resolve(string nodeType, EngineContext context)
    {
        var custom = context.NodeBehaviors.Where(item => item.NodeType == nodeType).Take(2).ToArray();
        if (custom.Length > 1)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "同一节点类型重复注册了行为。");
        }

        if (custom.Length == 1)
        {
            return custom[0];
        }

        return BuiltIns.TryGetValue(nodeType, out var builtIn)
            ? builtIn
            : throw new EngineException(ErrorCodes.ValidationFailed, "执行图含未注册的节点类型。");
    }
}
