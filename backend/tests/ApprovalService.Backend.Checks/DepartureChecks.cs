using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 离职在途交接命令的节点规则、授权与幂等场景。
/// </summary>
internal static class DepartureChecks
{
    /// <summary>
    /// 运行离职处理的纯引擎场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var state = new EngineState
        {
            InstanceId = "departure-instance",
            Status = EngineInstanceStatuses.Running,
            Tasks =
            [
                new EngineTask { Id = "task-manager", TokenId = "token-a", NodeId = "manager-node",
                    AssigneeId = "departed", Status = EngineTaskStatuses.Pending },
                new EngineTask { Id = "task-admin", TokenId = "token-b", NodeId = "admin-node",
                    AssigneeId = "departed", Status = EngineTaskStatuses.Queued },
            ],
        };
        var context = new EngineContext
        {
            OccurredAt = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero),
            Graph = new FlowExecutionGraph
            {
                Nodes =
                [
                    new FlowExecutionNode { Id = "manager-node", Type = FlowNodeTypes.Approval,
                        Approval = new FlowApprovalSettings { InactiveAssigneeBehavior = FlowInactiveAssigneeBehaviors.Manager } },
                    new FlowExecutionNode { Id = "admin-node", Type = FlowNodeTypes.Approval,
                        Approval = new FlowApprovalSettings { InactiveAssigneeBehavior = FlowInactiveAssigneeBehaviors.Administrator } },
                ],
            },
            AssigneeSnapshot = Snapshot(),
        };
        var managerCommand = Command("task-manager", "departure-manager");
        var manager = EngineRuntime.Execute(state, managerCommand, context);
        check("离职待办按节点默认配置转给在职直属上级并留事件",
            manager.State.Tasks[0].AssigneeId == "manager" &&
            manager.Events.Single().Kind == "inactiveTaskReassigned" &&
            manager.Events.Single().TargetCode == "manager" &&
            manager.Events.Single().Audit?.TargetUserId == "manager" &&
            manager.Events.Single().Audit?.Reason is not null &&
            state.Tasks[0].AssigneeId == "departed");
        var administrator = EngineRuntime.Execute(manager.State,
            Command("task-admin", "departure-admin"), context);
        check("同一审批单的另一节点可按配置转流程管理员且排队状态不变",
            administrator.State.Tasks[1].AssigneeId == "administrator" &&
            administrator.State.Tasks[1].Status == EngineTaskStatuses.Queued);
        var replay = EngineRuntime.Execute(administrator.State, managerCommand, context);
        check("离职命令同请求 ID 重试不重复产生事件", replay.IsReplay && replay.Events.Count == 0);
        check("过期任务不能再次改派", Throws(ErrorCodes.Conflict,
            () => EngineRuntime.Execute(administrator.State, Command("task-manager", "stale"), context)));

        var unauthorized = Command("task-manager", "unauthorized");
        unauthorized.Operation!.IsAdministrator = false;
        check("未经确认的离职系统命令不能改派", Throws(ErrorCodes.Forbidden,
            () => EngineRuntime.Execute(state, unauthorized, context)));
        var noReason = Command("task-manager", "no-reason");
        noReason.Operation!.Reason = null;
        check("离职交接必须记录原因", Throws(ErrorCodes.ValidationFailed,
            () => EngineRuntime.Execute(state, noReason, context)));
        context.AssigneeSnapshot!.People.Single(item => item.Id == "departed").IsActive = true;
        check("仍在职的人不能触发自动离职交接", Throws(ErrorCodes.Conflict,
            () => EngineRuntime.Execute(state, Command("task-manager", "active-person"), context)));
        context.AssigneeSnapshot.People.Single(item => item.Id == "departed").IsActive = false;
        context.AssigneeSnapshot.People.Single(item => item.Id == "manager").IsActive = false;
        check("直属上级停用且无在职上级时保持原任务", Throws(ErrorCodes.ValidationFailed,
            () => EngineRuntime.Execute(state, Command("task-manager", "missing-manager"), context)) &&
            state.Tasks[0].AssigneeId == "departed");
        context.AssigneeSnapshot.People.Single(item => item.Id == "administrator").IsActive = false;
        check("流程管理员停用时不能转给无效人员", Throws(ErrorCodes.ValidationFailed,
            () => EngineRuntime.Execute(state, Command("task-admin", "missing-admin"), context)));
    }

    /// <summary>
    /// 构造带原人员、上级和流程管理员的组织快照。
    /// </summary>
    /// <returns>供纯引擎场景使用的快照。</returns>
    private static AssigneeSnapshot Snapshot() => new()
    {
        AdministratorId = "administrator",
        People =
        [
            new AssigneePerson { Id = "departed", ManagerId = "manager", IsActive = false },
            new AssigneePerson { Id = "manager", IsActive = true },
            new AssigneePerson { Id = "administrator", IsActive = true },
        ],
    };

    /// <summary>
    /// 构造经过应用层授权标记的离职命令。
    /// </summary>
    /// <param name="taskId">要改派的任务。</param>
    /// <param name="requestId">幂等请求 ID。</param>
    /// <returns>离职命令。</returns>
    private static EngineCommand Command(string taskId, string requestId) => new()
    {
        Kind = OperationKinds.ReassignInactive,
        InstanceId = "departure-instance",
        TaskId = taskId,
        RequestId = requestId,
        Operation = new OperationInput
        {
            ActorId = "system:organization",
            RelatedId = "departed",
            Reason = "人员离职或停用。",
            IsAdministrator = true,
        },
    };

    /// <summary>
    /// 检查引擎拒绝结果是否带预期错误码。
    /// </summary>
    /// <param name="code">预期错误码。</param>
    /// <param name="action">待执行场景。</param>
    /// <returns>错误码匹配为真。</returns>
    private static bool Throws(string code, Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (EngineException error)
        {
            return error.Code == code;
        }
    }
}
