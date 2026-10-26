using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Center;

/// <summary>
/// 从已授权审批单的锁定发布版本投影到达路径与当前人员可选目标。
/// </summary>
internal static class CenterProgressProjection
{
    /// <summary>
    /// 只返回已经进入过或当前运行的节点，隐藏未来分支和节点规则。
    /// </summary>
    /// <param name="record">已经按人员和应用授权的详情。</param>
    /// <param name="definition">发起时锁定的设计定义。</param>
    /// <param name="graph">发起时锁定的执行图。</param>
    /// <param name="state">当前执行状态。</param>
    /// <returns>安全进度图。</returns>
    public static CenterProgressResponse Progress(CenterRecord record, FlowDefinition definition,
        FlowExecutionGraph graph, EngineState state)
    {
        var reached = record.Events.Where(item => item.Kind == "nodeEntered")
            .Select(item => JsonSerializer.Deserialize<EngineEvent>(item.PayloadJson, ApprovalJson.Options)?.NodeId)
            .OfType<string>().Concat(state.Tokens.Select(token => token.NodeId))
            .Concat(record.Tasks.Select(task => task.NodeId)).ToHashSet(StringComparer.Ordinal);
        var active = state.Tokens.Select(token => token.NodeId).ToHashSet(StringComparer.Ordinal);
        var positions = definition.Canvas.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var nodes = graph.Nodes.Where(node => reached.Contains(node.Id)).Select((node, index) =>
        {
            positions.TryGetValue(node.Id, out var position);
            return new CenterProgressNodeResponse
            {
                Id = node.Id,
                Type = node.Type,
                Name = node.Name,
                X = position is not null && double.IsFinite(position.X) ? position.X : index * 190,
                Y = position is not null && double.IsFinite(position.Y) ? position.Y : 0,
                IsActive = active.Contains(node.Id),
            };
        }).ToArray();
        return new CenterProgressResponse
        {
            Nodes = nodes,
            Edges = graph.Edges.Where(edge => reached.Contains(edge.SourceId) && reached.Contains(edge.TargetId))
                .Select(edge => new CenterProgressEdgeResponse
                {
                    Id = edge.Id,
                    SourceId = edge.SourceId,
                    TargetId = edge.TargetId,
                }).ToArray(),
        };
    }

    /// <summary>
    /// 只为当前人员实际获准的操作枚举关联目标，提交时引擎仍重新核验。
    /// </summary>
    /// <param name="state">当前执行状态。</param>
    /// <param name="graph">锁定执行图。</param>
    /// <param name="task">投影中的人工任务。</param>
    /// <param name="personId">已认证人员 ID。</param>
    /// <param name="allowed">服务端本次确认的可尝试命令。</param>
    /// <returns>仅属于当前人员任务的关联目标。</returns>
    public static IReadOnlyList<CenterRelatedTargetResponse> Targets(EngineState state,
        FlowExecutionGraph graph, EngineTask task, string personId, IReadOnlyList<string> allowed)
    {
        var result = new List<CenterRelatedTargetResponse>();
        if (allowed.Contains(OperationKinds.Reply, StringComparer.Ordinal))
        {
            var consultations = state.Operations.Consultations.Where(item => item.TaskId == task.Id &&
                item.RecipientId == personId && !item.Replied).ToArray();
            for (var index = 0; index < consultations.Length; index++)
            {
                result.Add(new CenterRelatedTargetResponse
                {
                    Action = OperationKinds.Reply,
                    Id = consultations[index].Id,
                    Label = $"待回复征询 {index + 1}",
                });
            }
        }
        if (allowed.Contains(OperationKinds.RemoveSigner, StringComparer.Ordinal))
        {
            foreach (var signer in state.Tasks.Where(item => item.AddedByTaskId == task.Id &&
                item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued))
            {
                result.Add(new CenterRelatedTargetResponse
                {
                    Action = OperationKinds.RemoveSigner,
                    Id = signer.Id,
                    Label = graph.Nodes.SingleOrDefault(node => node.Id == signer.NodeId)?.Name ?? "未处理加签",
                });
            }
        }
        if (allowed.Contains(OperationKinds.Return, StringComparer.Ordinal))
        {
            var currentForks = state.Tokens.SingleOrDefault(token => token.Id == task.TokenId)?.Forks;
            foreach (var nodeId in state.Operations.Visits.Where(visit =>
                visit.NodeId != task.NodeId && currentForks is not null && visit.Forks.Count <= currentForks.Count &&
                visit.Forks.Select((frame, index) => frame.ForkId == currentForks[index].ForkId &&
                    frame.BranchEdgeId == currentForks[index].BranchEdgeId).All(same => same))
                .Select(visit => visit.NodeId).Distinct(StringComparer.Ordinal))
            {
                result.Add(new CenterRelatedTargetResponse
                {
                    Action = OperationKinds.Return,
                    Id = nodeId,
                    Label = graph.Nodes.SingleOrDefault(node => node.Id == nodeId)?.Name ?? nodeId,
                });
            }
        }
        return result;
    }
}
