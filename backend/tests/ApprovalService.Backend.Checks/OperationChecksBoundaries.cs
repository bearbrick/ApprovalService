using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 各操作的拒绝路径与跨命令状态边界。
/// </summary>
internal static class OperationChecksBoundaries
{
    /// <summary>
    /// 运行非法身份、状态、目标和频率的场景。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    public static void Run(Action<string, bool> check)
    {
        CheckInitiatorAndAdmin(check);
        CheckTaskActions(check);
        CheckDelegationAndBatch(check);
    }

    /// <summary>
    /// 检查发起人和管理员的授权边界。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckInitiatorAndAdmin(Action<string, bool> check)
    {
        var context = Context(EngineScenarioChecks.Chain(FlowNodeTypes.Approval));
        var started = Start(context);
        var task = started.State.Tasks.Single();
        check("非发起人不能撤回", Throws(ErrorCodes.Forbidden, () => Execute(started.State, context,
            Command(EngineCommandKinds.Withdraw, "wrong-withdraw", "other"))));
        var withdrawn = Execute(started.State, context,
            Command(EngineCommandKinds.Withdraw, "withdraw", "initiator"));
        check("发起人可在审批中撤回", withdrawn.State.Status == EngineInstanceStatuses.Withdrawn &&
            withdrawn.Events.Any(item => item.Kind == "instanceWithdraw"));
        check("已撤回后不能继续同意", Throws(ErrorCodes.Conflict, () => Execute(withdrawn.State, context,
            Command(EngineCommandKinds.Approve, "after-withdraw", task.AssigneeId, task.Id))));
        check("非发起人不能催办", Throws(ErrorCodes.Forbidden, () => Execute(started.State, context,
            Command(OperationKinds.Remind, "wrong-remind", "other"))));
        check("尚未退回不能重新提交", Throws(ErrorCodes.Conflict, () => Execute(started.State, context,
            Command(OperationKinds.Resubmit, "unneeded-resubmit", "initiator"))));
        check("尚未要求补充不能提交补充", Throws(ErrorCodes.Conflict, () => Execute(started.State, context,
            Command(OperationKinds.Supplement, "unneeded-supplement", "initiator"))));

        var terminate = Command(EngineCommandKinds.Terminate, "terminate", "admin");
        terminate.Operation!.IsAdministrator = true;
        check("管理员终止缺少原因会被拒绝", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, terminate)));
        terminate.Operation.Reason = "异常单据";
        var terminated = Execute(started.State, context, terminate);
        check("管理员终止留下原因且取消任务", terminated.State.Status == EngineInstanceStatuses.Terminated &&
            terminated.State.Tasks.Single().Status == EngineTaskStatuses.Cancelled &&
            terminated.Events.Any(item => item.Audit?.Reason == "异常单据"));
        var ownerTerminate = Command(EngineCommandKinds.Terminate, "owner-terminate", "bound-operator");
        ownerTerminate.Operation!.IsOwningClientApp = true;
        check("所属应用终止缺少原因会被拒绝", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, ownerTerminate)));
        ownerTerminate.Operation.Reason = "业务单据作废";
        var ownerTerminated = Execute(started.State, context, ownerTerminate);
        check("所属应用可终止并记录实际操作人和原因",
            ownerTerminated.State.Status == EngineInstanceStatuses.Terminated &&
            ownerTerminated.Events.Any(item => item.Audit is
            { ActorId: "bound-operator", Reason: "业务单据作废" }));
        var supplementPending = Execute(started.State, context,
            Command(OperationKinds.RequestSupplement, "request-supplement", task.AssigneeId, task.Id));
        var supplementTerminated = Execute(supplementPending.State, context, ownerTerminate);
        check("所属应用可终止待补充的在途审批并清除暂停入口",
            supplementPending.State.Status == EngineInstanceStatuses.AwaitingInitiator &&
            supplementTerminated.State.Status == EngineInstanceStatuses.Terminated &&
            supplementTerminated.State.Operations.Pause is null &&
            supplementTerminated.State.Tasks.All(item => item.Status != EngineTaskStatuses.Pending));
        var returnedPending = Execute(started.State, context,
            Command(OperationKinds.Return, "return-to-initiator", task.AssigneeId, task.Id));
        var returnedTerminated = Execute(returnedPending.State, context, ownerTerminate);
        check("所属应用可终止退回待重提的在途审批",
            returnedPending.State.Status == EngineInstanceStatuses.AwaitingInitiator &&
            returnedTerminated.State.Status == EngineInstanceStatuses.Terminated &&
            returnedTerminated.State.Operations.Pause is null);
        check("等待发起人时撤回仍遵守原状态限制", Throws(ErrorCodes.Conflict, () =>
            Execute(returnedPending.State, context,
                Command(EngineCommandKinds.Withdraw, "awaiting-withdraw", "initiator"))));
        var unauthorizedTerminate = Command(EngineCommandKinds.Terminate, "unknown-terminate", "unknown");
        unauthorizedTerminate.Operation!.Reason = "作废";
        check("未核准管理员或所属应用时不能终止", Throws(ErrorCodes.Forbidden, () =>
            Execute(started.State, context, unauthorizedTerminate)));
        var ownerJump = Command(OperationKinds.Jump, "owner-jump", "bound-operator");
        ownerJump.Operation!.IsOwningClientApp = true;
        ownerJump.Operation.Reason = "尝试越权";
        ownerJump.Operation.TargetNodeId = "n1";
        check("所属应用终止权限不能扩展成管理员跳转", Throws(ErrorCodes.Forbidden, () =>
            Execute(started.State, context, ownerJump)));

        var handover = Admin(OperationKinds.Handover, "handover", "人员离职");
        handover.Operation!.RelatedId = task.AssigneeId;
        handover.Operation.TargetUserId = "successor";
        var handed = Execute(started.State, context, handover);
        check("管理员交接在途任务给接手人", handed.State.Tasks.Single().AssigneeId == "successor" &&
            handed.Events.Any(item => item.Kind == "taskHandedOver"));
        handover.Operation.RelatedId = "unknown";
        check("没有在途任务时不能交接", Throws(ErrorCodes.Conflict, () =>
            Execute(started.State, context, handover)));

        var badJump = Admin(OperationKinds.Jump, "join", "纠正路径");
        badJump.Operation!.TargetNodeId = "absent";
        check("管理员不能跳转到不存在的节点", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, badJump)));
        foreach (var gatewayType in new[] { FlowNodeTypes.ParallelGateway, FlowNodeTypes.InclusiveGateway })
        {
            var gatewayContext = Context(EngineScenarioChecks.GatewayGraph(gatewayType));
            var gatewayStarted = Start(gatewayContext);
            var joinJump = Admin(OperationKinds.Jump, "join-" + gatewayType, "纠正路径");
            joinJump.Operation!.TargetNodeId = "join";
            check("管理员不能直接跳到" + gatewayType + "汇合网关", Throws(ErrorCodes.ValidationFailed, () =>
                Execute(gatewayStarted.State, gatewayContext, joinJump)));
        }
        var resend = Admin(OperationKinds.Resend, "resend", "回调遗失");
        resend.Operation!.Mode = "callback";
        var resent = Execute(started.State, context, resend);
        check("管理员重发仅发出待发事件", resent.State.Tasks.Single().Status == EngineTaskStatuses.Pending &&
            resent.Events.Any(item => item.Kind == "outboundResendRequested" && item.TargetCode == "callback"));
        resend.Operation.Mode = null;
        check("重发缺少目标类型会被拒绝", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, resend)));
    }

    /// <summary>
    /// 检查当前审批人的扩展操作不能越权或接受无效目标。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckTaskActions(Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
        graph.Nodes[1].Approval = new FlowApprovalSettings { CommentRequired = true };
        var context = Context(graph);
        var started = Start(context);
        var task = started.State.Tasks.Single();
        foreach (var kind in new[] { OperationKinds.Transfer, OperationKinds.AddSigner,
            OperationKinds.Consult, OperationKinds.CopyTo, OperationKinds.Comment,
            OperationKinds.SaveDraft, OperationKinds.RequestSupplement, OperationKinds.Return })
        {
            var command = Command(kind, "unauthorized-" + kind, "other", task.Id);
            command.Operation!.Opinion = "已有意见";
            command.Operation.TargetUserId = "target";
            check("非当前审批人不能执行 " + kind, Throws(ErrorCodes.Forbidden, () =>
                Execute(started.State, context, command)));
        }

        check("转交缺少目标人员会被拒绝", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, Command(OperationKinds.Transfer, "no-target", task.AssigneeId, task.Id))));
        check("加签不能把自己作为目标", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, Target(OperationKinds.AddSigner, "self-signer", task, task.AssigneeId))));
        check("征询缺少受询人会被拒绝", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, Command(OperationKinds.Consult, "no-recipient", task.AssigneeId, task.Id))));
        check("评论缺少内容会被拒绝", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, Command(OperationKinds.Comment, "empty-comment", task.AssigneeId, task.Id))));
        check("抄送缺少收件人会被拒绝", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, Command(OperationKinds.CopyTo, "empty-cc", task.AssigneeId, task.Id))));
        check("意见必填节点不能无意见退回", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, Command(OperationKinds.Return, "empty-return", task.AssigneeId, task.Id))));
        check("意见必填节点不能无意见要求补充", Throws(ErrorCodes.ValidationFailed, () =>
            Execute(started.State, context, Command(OperationKinds.RequestSupplement, "empty-supplement", task.AssigneeId, task.Id))));
        var badReturn = Command(OperationKinds.Return, "bad-return", task.AssigneeId, task.Id);
        badReturn.Operation!.Opinion = "请重审";
        badReturn.Operation.TargetNodeId = "n2";
        check("不能退回到尚未走过的节点", Throws(ErrorCodes.Conflict, () =>
            Execute(started.State, context, badReturn)));

        graph.Nodes[1].Approval!.AllowedActions = [EngineCommandKinds.Approve];
        check("节点禁止转交时即使是审批人也不能转交", Throws(ErrorCodes.Conflict, () =>
            Execute(started.State, context, Target(OperationKinds.Transfer, "blocked-transfer", task, "new-user"))));
        var consultationGraph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        var consultationContext = Context(consultationGraph);
        var pending = Start(consultationContext);
        var pendingTask = pending.State.Tasks.Single();
        var consult = Execute(pending.State, consultationContext,
            Target(OperationKinds.Consult, "consult", pendingTask, "expert"));
        var reply = Command(OperationKinds.Reply, "wrong-reply", "other");
        reply.TaskId = pendingTask.Id;
        reply.Operation!.RelatedId = consult.State.Operations.Consultations.Single().Id;
        reply.Operation.Opinion = "回复";
        check("非受询人不能回复征询", Throws(ErrorCodes.Forbidden, () =>
            Execute(consult.State, consultationContext, reply)));
    }

    /// <summary>
    /// 检查无生效委托与批量同意的安全边界。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckDelegationAndBatch(Action<string, bool> check)
    {
        var context = Context(EngineScenarioChecks.Chain(FlowNodeTypes.Approval));
        var started = Start(context);
        var task = started.State.Tasks.Single();
        check("没有生效委托时不能转交旧待办", Throws(ErrorCodes.Conflict, () =>
            Execute(started.State, context,
                Command(OperationKinds.DelegateExisting, "no-delegation", task.AssigneeId, task.Id))));
        check("批量同意不能为空", Throws(ErrorCodes.ValidationFailed, () => BatchApproval.Execute([])));
        var first = new BatchApprovalItem
        {
            State = started.State,
            Context = context,
            Command = Command(EngineCommandKinds.Approve, "batch-one", task.AssigneeId, task.Id),
        };
        var otherState = EngineRuntime.Execute(null,
            new EngineCommand { Kind = EngineCommandKinds.Start, InstanceId = "other-instance", RequestId = "other-start" },
            context).State;
        var otherTask = otherState.Tasks.Single();
        var second = new BatchApprovalItem
        {
            State = otherState,
            Context = context,
            Command = new EngineCommand
            {
                Kind = EngineCommandKinds.Approve,
                InstanceId = "other-instance",
                RequestId = "batch-two",
                TaskId = otherTask.Id,
                Operation = new OperationInput { ActorId = "another-actor" },
            },
        };
        check("一批审批不能混用不同操作人", Throws(ErrorCodes.ValidationFailed, () =>
            BatchApproval.Execute([first, second])));
        check("批量校验失败不改写任一原状态", started.State.Tasks.Single().Status == EngineTaskStatuses.Pending &&
            otherState.Tasks.Single().Status == EngineTaskStatuses.Pending);
    }

    /// <summary>
    /// 建立带身份的固定上下文。
    /// </summary>
    /// <param name="graph">执行图。</param>
    /// <returns>纯运行上下文。</returns>
    private static EngineContext Context(FlowExecutionGraph graph)
    {
        var context = EngineScenarioChecks.Context(graph);
        context.Operations = new OperationContext { InitiatorId = "initiator", FlowCode = "FLOW" };
        return context;
    }

    /// <summary>
    /// 发起一张审批单。
    /// </summary>
    /// <param name="context">运行上下文。</param>
    /// <returns>引擎结果。</returns>
    private static EngineResult Start(EngineContext context) => EngineRuntime.Execute(null,
        EngineScenarioChecks.Command(EngineCommandKinds.Start, "start"), context);

    /// <summary>
    /// 构造带实际操作人的实例命令。
    /// </summary>
    /// <param name="kind">操作编码。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="actor">实际操作人。</param>
    /// <param name="taskId">任务 ID。</param>
    /// <returns>命令。</returns>
    private static EngineCommand Command(string kind, string requestId, string actor, string? taskId = null) => new()
    {
        Kind = kind,
        RequestId = requestId,
        InstanceId = "instance",
        TaskId = taskId,
        Operation = new OperationInput { ActorId = actor },
    };

    /// <summary>
    /// 构造有目标人员的当前任务命令。
    /// </summary>
    /// <param name="kind">操作编码。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="task">当前任务。</param>
    /// <param name="target">目标人员。</param>
    /// <returns>命令。</returns>
    private static EngineCommand Target(string kind, string requestId, EngineTask task, string target)
    {
        var command = Command(kind, requestId, task.AssigneeId, task.Id);
        command.Operation!.TargetUserId = target;
        return command;
    }

    /// <summary>
    /// 构造管理员命令并提供原因。
    /// </summary>
    /// <param name="kind">操作编码。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="reason">干预原因。</param>
    /// <returns>命令。</returns>
    private static EngineCommand Admin(string kind, string requestId, string reason)
    {
        var command = Command(kind, requestId, "admin");
        command.Operation!.IsAdministrator = true;
        command.Operation.Reason = reason;
        return command;
    }

    /// <summary>
    /// 通过唯一入口执行操作。
    /// </summary>
    /// <param name="state">当前状态。</param>
    /// <param name="context">运行上下文。</param>
    /// <param name="command">命令。</param>
    /// <returns>引擎结果。</returns>
    private static EngineResult Execute(EngineState state, EngineContext context, EngineCommand command) =>
        EngineRuntime.Execute(state, command, context);

    /// <summary>
    /// 判断是否按指定错误码拒绝。
    /// </summary>
    /// <param name="code">预期错误码。</param>
    /// <param name="action">待执行操作。</param>
    /// <returns>按预期拒绝时为真。</returns>
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
