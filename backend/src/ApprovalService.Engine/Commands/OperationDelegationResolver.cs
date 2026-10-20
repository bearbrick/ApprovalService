using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 按生效时间和流程范围选择一层代理人，不沿代理人的委托继续查找。
/// </summary>
internal static class OperationDelegationResolver
{
    /// <summary>
    /// 将当前或排队中的已有任务按生效委托交给代理人。
    /// </summary>
    /// <param name="state">审批状态副本。</param>
    /// <param name="command">委托命令。</param>
    /// <param name="context">固定的委托和组织快照。</param>
    /// <param name="work">事件计算区。</param>
    public static void ApplyExisting(EngineState state, EngineCommand command, EngineContext context, EngineWork work)
    {
        if (state.Status != EngineInstanceStatuses.Running)
        {
            throw new EngineException(ErrorCodes.Conflict, "审批单不在审批中。");
        }

        var task = state.Tasks.SingleOrDefault(item => item.Id == command.TaskId &&
            item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued)
            ?? throw new EngineException(ErrorCodes.Conflict, "任务不存在或已处理。");
        if (task.DelegatorId is not null)
        {
            throw new EngineException(ErrorCodes.Conflict, "任务已经委托，不能连环委托。");
        }

        var node = context.Graph.Nodes.Single(item => item.Id == task.NodeId);
        var assignment = Resolve(task.AssigneeId, node, context);
        if (assignment.DelegatorId is null)
        {
            throw new EngineException(ErrorCodes.Conflict, "当前任务没有生效的委托设置。");
        }

        if (assignment.AssigneeId is null)
        {
            var token = state.Tokens.Single(item => item.Id == task.TokenId);
            work.Emit("delegatedSelfApprovalSkipped", task.NodeId, task.TokenId, task.Id);
            work.CompleteTask(task, token, node, task.CompletionKind);
            return;
        }

        task.DelegatorId = assignment.DelegatorId;
        task.AssigneeId = assignment.AssigneeId;
        work.Emit("existingTaskDelegated", task.NodeId, task.TokenId, task.Id);
    }

    /// <summary>
    /// 计算一项新任务的实际处理人与原审批人。
    /// </summary>
    /// <param name="principalId">节点原审批人。</param>
    /// <param name="node">当前节点。</param>
    /// <param name="context">固定的组织与委托快照。</param>
    /// <returns>实际处理人及原审批人；跳过时处理人为空。</returns>
    public static (string? AssigneeId, string? DelegatorId) Resolve(string principalId, FlowExecutionNode node, EngineContext context)
    {
        var operations = context.Operations;
        var active = operations.Delegations.Where(item => item.PrincipalId == principalId &&
            item.StartsAt <= context.OccurredAt && context.OccurredAt < item.EndsAt &&
            (item.FlowCodes.Count == 0 || item.FlowCodes.Contains(operations.FlowCode, StringComparer.Ordinal))).ToArray();
        if (active.Length > 1)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "同一审批人在当前流程和时间有多项生效委托。");
        }

        if (active.Length == 0)
        {
            return (principalId, null);
        }

        var delegateId = active[0].DelegateId;
        if (string.IsNullOrWhiteSpace(delegateId) || delegateId == principalId || active[0].EndsAt <= active[0].StartsAt)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "委托代理人或有效期无效。");
        }

        // 故意只查委托人的配置一次：代理人再委托也不形成连环代理。
        if (delegateId != operations.InitiatorId)
        {
            return (delegateId, principalId);
        }

        var behavior = node.Approval?.SelfApprovalBehavior ?? FlowSelfApprovalBehaviors.Skip;
        if (behavior == FlowSelfApprovalBehaviors.Self)
        {
            return (delegateId, principalId);
        }

        if (behavior == FlowSelfApprovalBehaviors.Skip)
        {
            return (null, principalId);
        }

        if (behavior == FlowSelfApprovalBehaviors.Manager)
        {
            var person = context.AssigneeSnapshot?.People.SingleOrDefault(item => item.Id == delegateId);
            if (person?.ManagerId is null)
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "代理人是发起人，但没有可用的直属上级。");
            }

            return (person.ManagerId, principalId);
        }

        throw new EngineException(ErrorCodes.ValidationFailed, "未知的本人审批处理方式。");
    }
}
