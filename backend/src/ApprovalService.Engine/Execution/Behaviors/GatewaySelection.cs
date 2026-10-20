using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Engine.Execution.Behaviors;

/// <summary>
/// 成对网关的分支排序和条件选择，所有分叉行为共用同一规则。
/// </summary>
internal static class GatewaySelection
{
    /// <summary>
    /// 选择排他、包容或并行网关的实际激活连线。
    /// </summary>
    /// <param name="node">当前网关。</param>
    /// <param name="context">已求值的条件快照。</param>
    /// <returns>按优先级排列的连线 ID。</returns>
    public static IReadOnlyList<string> Select(FlowExecutionNode node, EngineContext context)
    {
        var edges = context.Graph.Edges.Where(item => item.SourceId == node.Id)
            .OrderBy(item => item.Priority).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
        if (edges.Count < 2)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "分叉网关至少需要两条出线。");
        }

        if (node.Type == FlowNodeTypes.ParallelGateway)
        {
            return edges.Select(item => item.Id).ToArray();
        }

        // 缺少求值结果不能默认为不命中，否则会误走兜底审批路径。
        foreach (var edge in edges.Where(item => !item.IsDefault))
        {
            if (!context.Conditions.ContainsKey(edge.Id))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "条件网关缺少连线求值结果。");
            }
        }

        var selected = edges.Where(edge => !edge.IsDefault && context.Conditions[edge.Id]).ToList();
        if (selected.Count == 0)
        {
            selected = edges.Where(edge => edge.IsDefault).ToList();
        }
        else if (node.Type == FlowNodeTypes.ExclusiveGateway)
        {
            selected = [selected[0]];
        }

        if (selected.Count == 0)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "条件网关没有可走的分支。");
        }

        return selected.Select(item => item.Id).ToArray();
    }
}
