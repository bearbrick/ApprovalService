using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 从纯运行状态和固定发布图计算当前人员可尝试的任务操作；提交时仍走统一命令校验。
/// </summary>
public static class TaskActionAvailability
{
    /// <summary>
    /// 返回任务当前可呈现的命令编码，不以此替代提交时的最终校验。
    /// </summary>
    /// <param name="state">已持久化的引擎状态。</param>
    /// <param name="graph">发起时锁定的发布图。</param>
    /// <param name="taskId">任务标识。</param>
    /// <param name="actorId">已认证人员 ID。</param>
    /// <returns>当前人员可尝试的命令编码。</returns>
    public static IReadOnlyList<string> GetAllowedActions(EngineState state, FlowExecutionGraph graph,
        string taskId, string actorId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var task = state.Tasks.SingleOrDefault(item => item.Id == taskId);
        if (task is null || state.Status != EngineInstanceStatuses.Running)
        {
            return [];
        }

        var result = new List<string>();
        if (task.Status == EngineTaskStatuses.Pending &&
            state.Operations.Consultations.Any(item => item.TaskId == taskId && item.RecipientId == actorId && !item.Replied))
        {
            result.Add(OperationKinds.Reply);
        }

        var ownsTask = task.AssigneeId == actorId || task.DelegatorId == actorId;
        if (!ownsTask)
        {
            return result;
        }

        if (task.Status == EngineTaskStatuses.Approved &&
            state.Operations.LastApprovalUndo is { } frame && frame.TaskId == taskId &&
            frame.ActorId == actorId && state.Operations.HumanDecisionCount == frame.DecisionCountAfter &&
            state.ProcessedRequestIds.Count == frame.ProcessedRequestCountAfter)
        {
            result.Add(OperationKinds.RevokeApproval);
        }

        var node = graph.Nodes.SingleOrDefault(item => item.Id == task.NodeId);
        if (node is null)
        {
            return result;
        }

        var configured = node.Approval?.AllowedActions;
        if ((task.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued or EngineTaskStatuses.Approved) &&
            state.Tasks.Any(item => item.AddedByTaskId == taskId &&
                item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued) &&
            TaskActionPermission.Permits(configured, node.Type, OperationKinds.RemoveSigner))
        {
            result.Add(OperationKinds.RemoveSigner);
        }

        if (task.Status != EngineTaskStatuses.Pending ||
            !state.Tokens.Any(item => item.Id == task.TokenId && item.WaitingFor == "task"))
        {
            return result;
        }

        if (!string.IsNullOrWhiteSpace(task.CompletionKind) &&
            TaskActionPermission.Permits(configured, node.Type, task.CompletionKind))
        {
            result.Add(task.CompletionKind);
            if (task.CompletionKind == EngineCommandKinds.Approve && node.Approval?.SignatureRequired != true &&
                TaskActionPermission.Permits(configured, node.Type, OperationKinds.BulkApprove))
            {
                result.Add(OperationKinds.BulkApprove);
            }
        }

        if (task.CanReject && TaskActionPermission.Permits(configured, node.Type, EngineCommandKinds.Reject))
        {
            result.Add(EngineCommandKinds.Reject);
        }

        foreach (var candidate in new[] { OperationKinds.Return, OperationKinds.Transfer, OperationKinds.AddSigner,
            OperationKinds.Consult, OperationKinds.RequestSupplement, OperationKinds.CopyTo,
            OperationKinds.Comment, OperationKinds.SaveDraft })
        {
            if (TaskActionPermission.Permits(configured, node.Type, candidate) &&
                (candidate != OperationKinds.RequestSupplement || state.Operations.Pause is null))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

}
