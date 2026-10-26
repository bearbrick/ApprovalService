using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 检查设计器办理动作与已发布执行图命令的兼容权限。
/// </summary>
internal static class HandlingActionChecks
{
    /// <summary>
    /// 在真实引擎任务上验证许可呈现和实际推进使用相同的别名规则。
    /// </summary>
    /// <param name="check">场景断言入口。</param>
    public static void Run(Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Handling);
        graph.Nodes.Single(item => item.Id == "n1").Approval = new FlowApprovalSettings
        {
            AllowedActions = [FlowActionKinds.Submit],
        };
        var context = EngineScenarioChecks.Context(graph);
        context.Operations = new OperationContext { InitiatorId = "initiator", FlowCode = "HANDLING" };
        var started = EngineRuntime.Execute(null, Start("handling-legacy"), context);
        var task = started.State.Tasks.Single();
        var available = TaskActionAvailability.GetAllowedActions(started.State, graph, task.Id, task.AssigneeId);
        check("存量办理 submit 配置显示真实 handle 命令且不显示其他决定", task.CompletionKind ==
            EngineCommandKinds.Handle && available.Contains(EngineCommandKinds.Handle) &&
            !available.Contains(EngineCommandKinds.Approve) && !available.Contains(EngineCommandKinds.Reject));
        var completed = EngineRuntime.Execute(started.State,
            Decide("handling-legacy", "complete", task.Id, task.AssigneeId, EngineCommandKinds.Handle), context);
        check("存量办理 submit 配置经正式引擎完成而不改原配置", completed.State.Status ==
            EngineInstanceStatuses.Approved && graph.Nodes.Single(item => item.Id == "n1")
                .Approval?.AllowedActions.SequenceEqual([FlowActionKinds.Submit]) == true);

        var direct = EngineScenarioChecks.Chain(FlowNodeTypes.Handling);
        direct.Nodes.Single(item => item.Id == "n1").Approval = new FlowApprovalSettings
        {
            AllowedActions = [EngineCommandKinds.Handle],
        };
        var directContext = EngineScenarioChecks.Context(direct);
        directContext.Operations = new OperationContext { InitiatorId = "initiator", FlowCode = "HANDLING" };
        var directStart = EngineRuntime.Execute(null, Start("handling-direct"), directContext);
        var directTask = directStart.State.Tasks.Single();
        check("直接配置 handle 的流程仍显示且完成办理", TaskActionAvailability.GetAllowedActions(
            directStart.State, direct, directTask.Id, directTask.AssigneeId).Contains(EngineCommandKinds.Handle) &&
            EngineRuntime.Execute(directStart.State, Decide("handling-direct", "complete", directTask.Id,
                directTask.AssigneeId, EngineCommandKinds.Handle), directContext).State.Status ==
            EngineInstanceStatuses.Approved);

        var approval = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        approval.Nodes.Single(item => item.Id == "n1").Approval = new FlowApprovalSettings
        {
            AllowedActions = [FlowActionKinds.Submit],
        };
        var approvalContext = EngineScenarioChecks.Context(approval);
        approvalContext.Operations = new OperationContext { InitiatorId = "initiator", FlowCode = "APPROVAL" };
        var approvalStart = EngineRuntime.Execute(null, Start("approval-submit"), approvalContext);
        var approvalTask = approvalStart.State.Tasks.Single();
        check("submit 别名不会给审批节点或无关命令扩大权限", !TaskActionAvailability.GetAllowedActions(
            approvalStart.State, approval, approvalTask.Id, approvalTask.AssigneeId).Contains(EngineCommandKinds.Approve) &&
            ThrowsConflict(() => EngineRuntime.Execute(approvalStart.State,
                Decide("approval-submit", "denied", approvalTask.Id, approvalTask.AssigneeId,
                    EngineCommandKinds.Approve), approvalContext)));
        check("办理别名不开放抄送节点及其他运行命令",
            !TaskActionPermission.Permits([FlowActionKinds.Submit], FlowNodeTypes.Cc, EngineCommandKinds.Handle) &&
            !TaskActionPermission.Permits([FlowActionKinds.Submit], FlowNodeTypes.Handling, EngineCommandKinds.Approve));
    }

    /// <summary>
    /// 建立唯一审批单的发起命令。
    /// </summary>
    /// <param name="instanceId">测试审批单 ID。</param>
    /// <returns>引擎发起命令。</returns>
    private static EngineCommand Start(string instanceId) => new()
    {
        Kind = EngineCommandKinds.Start,
        InstanceId = instanceId,
        RequestId = "start",
    };

    /// <summary>
    /// 建立带可信假处理人的人工命令。
    /// </summary>
    /// <param name="instanceId">测试审批单 ID。</param>
    /// <param name="requestId">命令请求 ID。</param>
    /// <param name="taskId">真实引擎产生的任务 ID。</param>
    /// <param name="actorId">当前任务处理人。</param>
    /// <param name="kind">引擎命令种类。</param>
    /// <returns>受统一守卫检查的命令。</returns>
    private static EngineCommand Decide(string instanceId, string requestId, string taskId, string actorId,
        string kind) => new()
        {
            Kind = kind,
            InstanceId = instanceId,
            RequestId = requestId,
            TaskId = taskId,
            Operation = new OperationInput { ActorId = actorId },
        };

    /// <summary>
    /// 核对节点拒绝未授权命令时保留冲突错误码。
    /// </summary>
    /// <param name="action">应失败的命令。</param>
    /// <returns>因权限冲突失败。</returns>
    private static bool ThrowsConflict(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (EngineException error)
        {
            return error.Code == ErrorCodes.Conflict;
        }
    }
}
