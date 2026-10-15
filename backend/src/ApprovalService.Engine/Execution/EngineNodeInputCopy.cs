using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Abstractions;

namespace ApprovalService.Engine.Execution;

/// <summary>
/// 为可插拔节点行为提供隔离输入，防止实现误改真实图和调用方上下文。
/// </summary>
internal static class EngineNodeInputCopy
{
    /// <summary>
    /// 深拷贝状态和执行图，复制条件、处理人字典；行为注册列表不属于节点求值输入。
    /// </summary>
    /// <param name="state">真实运行状态。</param>
    /// <param name="token">真实令牌。</param>
    /// <param name="context">调用方上下文。</param>
    /// <returns>节点行为独占的输入快照。</returns>
    public static EngineNodeInput Create(EngineState state, EngineToken token, EngineContext context)
    {
        // 已发布的执行图由 Contracts 的正式 JSON 约定完整复制，包含节点规则的嵌套集合。
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(
            JsonSerializer.Serialize(context.Graph, ApprovalJson.Options), ApprovalJson.Options)
            ?? throw new InvalidOperationException("执行图复制失败。");
        var stateCopy = EngineStateCopy.Copy(state);
        return new EngineNodeInput
        {
            Node = graph.Nodes.Single(item => item.Id == token.NodeId),
            Token = stateCopy.Tokens.Single(item => item.Id == token.Id),
            State = stateCopy,
            Context = new EngineContext
            {
                Graph = graph,
                OccurredAt = context.OccurredAt,
                Conditions = context.Conditions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                Assignees = context.Assignees.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                SubprocessDepth = context.SubprocessDepth,
            },
        };
    }
}
