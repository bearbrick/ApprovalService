using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 把组织停用事件映射为一项在途任务的引擎变更。
/// </summary>
internal static class InactiveAssigneeOperation
{
    /// <summary>
    /// 按任务所在节点的已发布配置，重分配停用人员的待办。
    /// </summary>
    /// <param name="state">本次命令的状态副本。</param>
    /// <param name="command">含任务 ID 和停用人员 ID 的命令。</param>
    /// <param name="context">发布图与当前组织快照。</param>
    /// <param name="work">事件计算区。</param>
    public static void Reassign(EngineState state, EngineCommand command, EngineContext context, EngineWork work)
    {
        if (state.Status != EngineInstanceStatuses.Running)
        {
            throw new EngineException(ErrorCodes.Conflict, "审批单不在审批中。");
        }

        var departedId = command.Operation?.RelatedId;
        if (string.IsNullOrWhiteSpace(departedId) || string.IsNullOrWhiteSpace(command.TaskId))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "离职交接缺少人员或任务。");
        }

        var task = state.Tasks.SingleOrDefault(item => item.Id == command.TaskId &&
            item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued);
        if (task is null || task.AssigneeId != departedId)
        {
            throw new EngineException(ErrorCodes.Conflict, "任务已处理或原审批人已变更。");
        }

        var snapshot = context.AssigneeSnapshot ??
            throw new EngineException(ErrorCodes.ValidationFailed, "离职交接缺少组织快照。");
        var person = snapshot.People.SingleOrDefault(item => item.Id == departedId);
        if (person is null || person.IsActive)
        {
            throw new EngineException(ErrorCodes.Conflict, "原审批人尚未停用或组织数据不完整。");
        }

        var node = context.Graph.Nodes.SingleOrDefault(item => item.Id == task.NodeId)
            ?? throw new EngineException(ErrorCodes.ValidationFailed, "任务所在节点不在发布的流程中。");
        var targetId = AssigneeRuleEngine.ResolveInactive(departedId,
            node.Approval ?? new FlowApprovalSettings(), snapshot);
        var target = snapshot.People.SingleOrDefault(item => item.Id == targetId);
        if (target is null || !target.IsActive || target.Id == departedId)
        {
            // 缺少可用上级或管理员时保持原任务，交给组织管理员修正组织数据后重试。
            throw new EngineException(ErrorCodes.ValidationFailed, "未找到在职接手人，请检查直属上级或流程管理员设置。");
        }

        task.AssigneeId = targetId;
        task.DelegatorId = null;
        work.Emit("inactiveTaskReassigned", task.NodeId, task.TokenId, task.Id, targetId);
    }
}
