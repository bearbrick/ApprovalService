using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 人工审批命令、代理、退回与干预的纯引擎场景。
/// </summary>
internal static class OperationChecks
{
    /// <summary>
    /// 运行操作权限、状态及事件场景。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    public static void Run(Action<string, bool> check)
    {
        CheckDecisions(check);
        CheckDelegation(check);
        CheckSimpleOperations(check);
        CheckSigners(check);
        CheckBatch(check);
        CheckReturns(check);
        CheckAdministration(check);
    }

    /// <summary>
    /// 检查任务身份、节点许可、意见与留痕。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckDecisions(Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
        graph.Nodes[1].Approval = new FlowApprovalSettings { CommentRequired = true, AllowedActions = [EngineCommandKinds.Approve] };
        var context = Context(graph);
        var started = Start(context);
        var task = started.State.Tasks.Single();
        check("绑定发起人后缺少操作人会被拒绝", Throws(ErrorCodes.ValidationFailed, () =>
            EngineRuntime.Execute(started.State, Command(EngineCommandKinds.Approve, "missing", task.Id), context)));
        check("非当前审批人不能同意", Throws(ErrorCodes.Forbidden, () =>
            EngineRuntime.Execute(started.State, Command(EngineCommandKinds.Approve, "wrong", task.Id, "other", "同意"), context)));
        check("节点不允许拒绝时会拒绝命令", Throws(ErrorCodes.Conflict, () =>
            EngineRuntime.Execute(started.State, Command(EngineCommandKinds.Reject, "blocked", task.Id, task.AssigneeId, "拒绝"), context)));
        check("意见必填节点拒绝空意见", Throws(ErrorCodes.ValidationFailed, () =>
            EngineRuntime.Execute(started.State, Command(EngineCommandKinds.Approve, "blank", task.Id, task.AssigneeId), context)));
        var accepted = EngineRuntime.Execute(started.State,
            Command(EngineCommandKinds.Approve, "accepted", task.Id, task.AssigneeId, "资料齐全"), context);
        check("同意事件记录实际操作人、时间和意见", accepted.Events.Any(item => item.Kind == "taskCompleted" &&
            item.Audit?.ActorId == task.AssigneeId && item.Audit.Opinion == "资料齐全" &&
            item.OccurredAt == context.OccurredAt));
        var revoke = EngineRuntime.Execute(accepted.State,
            Command(OperationKinds.RevokeApproval, "revoke", task.Id, task.AssigneeId), context);
        check("下一步未处理时本人可以撤销同意并恢复原任务", revoke.State.Tasks.Single().Status ==
            EngineTaskStatuses.Pending && revoke.Events.Any(item => item.Kind == "approvalRevoked") &&
            revoke.State.Operations.HumanDecisionCount == 0);
        check("其他人员不能撤销同意", Throws(ErrorCodes.Conflict, () => EngineRuntime.Execute(accepted.State,
            Command(OperationKinds.RevokeApproval, "not-owner", task.Id, "other"), context)));
        var next = accepted.State.Tasks.Last();
        var nextDone = EngineRuntime.Execute(accepted.State,
            Command(EngineCommandKinds.Approve, "next", next.Id, next.AssigneeId), context);
        check("下一步处理后无法撤销先前同意", Throws(ErrorCodes.Conflict, () => EngineRuntime.Execute(nextDone.State,
            Command(OperationKinds.RevokeApproval, "too-late", task.Id, task.AssigneeId), context)));
        check("处理后的旧任务不能再操作", Throws(ErrorCodes.Conflict, () =>
            EngineRuntime.Execute(accepted.State, Command(EngineCommandKinds.Approve, "again", task.Id,
                task.AssigneeId, "再试"), context)));
        check("失败命令没有改写输入状态", started.State.Tasks.Single().Status == EngineTaskStatuses.Pending &&
            started.State.Operations.HasHumanDecision == false);
    }

    /// <summary>
    /// 检查时间、流程范围、单层代理、发起人代理与本人可处理。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckDelegation(Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        var context = Context(graph);
        context.Operations.Delegations =
        [
            new OperationDelegation
            {
                PrincipalId = "user-n1", DelegateId = "proxy", StartsAt = context.OccurredAt.AddHours(-1),
                EndsAt = context.OccurredAt.AddHours(1), FlowCodes = ["FLOW"],
            },
            new OperationDelegation
            {
                PrincipalId = "proxy", DelegateId = "second", StartsAt = context.OccurredAt.AddHours(-1),
                EndsAt = context.OccurredAt.AddHours(1),
            },
        ];
        var delegated = Start(context);
        var task = delegated.State.Tasks.Single();
        check("有效委托只到第一层代理人", task.AssigneeId == "proxy" && task.DelegatorId == "user-n1");
        var acted = EngineRuntime.Execute(delegated.State,
            Command(EngineCommandKinds.Approve, "delegate-approve", task.Id, "proxy", "代办同意"), context);
        check("代理同意的事件保留代谁处理", acted.Events.Any(item => item.Kind == "taskCompleted" &&
            item.Audit?.ActorId == "proxy" && item.Audit.OnBehalfOfId == "user-n1"));
        var principalActed = EngineRuntime.Execute(delegated.State,
            Command(EngineCommandKinds.Approve, "principal-approve", task.Id, "user-n1", "本人同意"), context);
        check("委托期间原审批人仍可处理", principalActed.State.Status == EngineInstanceStatuses.Approved &&
            principalActed.Events.Any(item => item.Kind == "taskCompleted" && item.Audit?.OnBehalfOfId is null));

        var heldContext = Context(graph);
        var held = Start(heldContext);
        var heldTask = held.State.Tasks.Single();
        heldContext.Operations.Delegations = [context.Operations.Delegations[0]];
        var existing = EngineRuntime.Execute(held.State,
            Command(OperationKinds.DelegateExisting, "existing", heldTask.Id, heldTask.AssigneeId), heldContext);
        check("已在手的任务可选择交给有效代理人", existing.State.Tasks.Single().AssigneeId == "proxy" &&
            existing.State.Tasks.Single().DelegatorId == heldTask.AssigneeId);
        check("已代理任务不能再次连环委托", Throws(ErrorCodes.Conflict, () => EngineRuntime.Execute(existing.State,
            Command(OperationKinds.DelegateExisting, "chain", heldTask.Id, "proxy"), heldContext)));

        context.Operations.FlowCode = "OTHER";
        check("委托流程范围不匹配时原审批人接任务", Start(context).State.Tasks.Single().AssigneeId == "user-n1");
        context.Operations.FlowCode = "FLOW";
        context.OccurredAt = context.OccurredAt.AddHours(2);
        check("委托有效期结束后不再生效", Start(context).State.Tasks.Single().AssigneeId == "user-n1");
        context.OccurredAt = context.OccurredAt.AddHours(-2);
        context.Operations.Delegations[0].DelegateId = "initiator";
        check("代理人是发起人且配置跳过时直接通过", Start(context).State.Status == EngineInstanceStatuses.Approved);
        graph.Nodes[1].Approval = new FlowApprovalSettings { SelfApprovalBehavior = FlowSelfApprovalBehaviors.Self };
        check("代理人是发起人且配置本人审批时生成其任务", Start(context).State.Tasks.Single().AssigneeId == "initiator");
    }

    /// <summary>
    /// 检查转交、征询、暂存、抄送、补充与催办。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckSimpleOperations(Action<string, bool> check)
    {
        var context = Context(EngineScenarioChecks.Chain(FlowNodeTypes.Approval));
        var started = Start(context);
        var task = started.State.Tasks.Single();
        var transferred = EngineRuntime.Execute(started.State,
            Command(OperationKinds.Transfer, "transfer", task.Id, task.AssigneeId, target: "replacement"), context);
        check("转交后原审批人失去当前处理权", transferred.State.Tasks.Single().AssigneeId == "replacement" &&
            Throws(ErrorCodes.Forbidden, () => EngineRuntime.Execute(transferred.State,
                Command(EngineCommandKinds.Approve, "old", task.Id, task.AssigneeId), context)));
        var draft = EngineRuntime.Execute(started.State,
            Command(OperationKinds.SaveDraft, "draft", task.Id, task.AssigneeId, "写到一半"), context);
        check("意见暂存不推进任务", draft.State.Tasks.Single().Status == EngineTaskStatuses.Pending &&
            draft.State.Operations.DraftOpinions[task.Id] == "写到一半");
        var commented = EngineRuntime.Execute(started.State,
            Command(OperationKinds.Comment, "comment", task.Id, task.AssigneeId, "请协助"), context);
        check("评论产生带意见的事件", commented.Events.Any(item => item.Kind == "commentAdded" &&
            item.Audit?.Opinion == "请协助"));
        var copied = EngineRuntime.Execute(started.State,
            Command(OperationKinds.CopyTo, "copy", task.Id, task.AssigneeId, target: "observer"), context);
        check("抄送产生待发事件但不改变审批流", copied.State.Tasks.Single().Status == EngineTaskStatuses.Pending &&
            copied.Events.Any(item => item.Kind == "copyRequested" && item.Audit?.TargetUserId == "observer"));
        var consulted = EngineRuntime.Execute(started.State,
            Command(OperationKinds.Consult, "consult", task.Id, task.AssigneeId, target: "expert"), context);
        var inquiry = consulted.State.Operations.Consultations.Single();
        var reply = Command(OperationKinds.Reply, "reply", actor: "expert", opinion: "建议同意");
        reply.Operation!.RelatedId = inquiry.Id;
        var replied = EngineRuntime.Execute(consulted.State, reply, context);
        check("征询回复只由受询人完成且不推进审批", replied.State.Operations.Consultations.Single().Replied &&
            replied.State.Tasks.Single().Status == EngineTaskStatuses.Pending &&
            Throws(ErrorCodes.Conflict, () => EngineRuntime.Execute(replied.State,
                Command(OperationKinds.Reply, "again", actor: "expert", opinion: "重复"), context)));
        var supplement = EngineRuntime.Execute(started.State,
            Command(OperationKinds.RequestSupplement, "need", task.Id, task.AssigneeId), context);
        var supplied = EngineRuntime.Execute(supplement.State,
            Command(OperationKinds.Supplement, "supply", actor: "initiator"), context);
        check("补充后恢复原审批任务而不重走流程", supplement.State.Status == EngineInstanceStatuses.AwaitingInitiator &&
            supplied.State.Status == EngineInstanceStatuses.Running && supplied.State.Tasks.Count == 1 &&
            supplied.State.Tasks.Single().Id == task.Id);
        var reminded = EngineRuntime.Execute(started.State,
            Command(OperationKinds.Remind, "remind", actor: "initiator"), context);
        check("同一步一小时内不能重复催办", reminded.Events.Any(item => item.Kind == "reminderRequested") &&
            Throws(ErrorCodes.Conflict, () => EngineRuntime.Execute(reminded.State,
                Command(OperationKinds.Remind, "repeat", actor: "initiator"), context)));
    }

    /// <summary>
    /// 检查前、后、并行加签及未处理前减签。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckSigners(Action<string, bool> check)
    {
        var context = Context(EngineScenarioChecks.Chain(FlowNodeTypes.Approval));
        var started = Start(context);
        var original = started.State.Tasks.Single();
        foreach (var mode in new[] { "before", "after", "parallel" })
        {
            var add = Command(OperationKinds.AddSigner, "add-" + mode, original.Id, original.AssigneeId,
                target: "expert");
            add.Operation!.Mode = mode;
            var added = EngineRuntime.Execute(started.State, add, context);
            var signer = added.State.Tasks.Single(item => item.AddedByTaskId == original.Id);
            check("加签方式 " + mode + " 建立受控任务", signer.AssigneeId == "expert" &&
                signer.SignerMode == mode && added.Events.Any(item => item.Kind == "signerAdded"));
            var first = mode == "before" ? signer : added.State.Tasks.Single(item => item.Id == original.Id);
            var firstDone = EngineRuntime.Execute(added.State,
                Command(EngineCommandKinds.Approve, "first-" + mode, first.Id, first.AssigneeId), context);
            check("加签方式 " + mode + " 第一人同意后不会提前结束", firstDone.State.Status == EngineInstanceStatuses.Running);
            var last = firstDone.State.Tasks.Single(item => item.Status == EngineTaskStatuses.Pending);
            var completed = EngineRuntime.Execute(firstDone.State,
                Command(EngineCommandKinds.Approve, "last-" + mode, last.Id, last.AssigneeId), context);
            check("加签方式 " + mode + " 两人同意后才结束", completed.State.Status == EngineInstanceStatuses.Approved);

            var remove = Command(OperationKinds.RemoveSigner, "remove-" + mode, original.Id, original.AssigneeId);
            remove.Operation!.RelatedId = signer.Id;
            var removed = EngineRuntime.Execute(added.State, remove, context);
            check("减签只取消尚未处理的加签人 " + mode, removed.State.Tasks.Single(item => item.Id == signer.Id).Status ==
                EngineTaskStatuses.Cancelled && removed.Events.Any(item => item.Kind == "signerRemoved"));
            check("已处理加签人不能被减签 " + mode, Throws(ErrorCodes.Conflict, () =>
                EngineRuntime.Execute(completed.State, remove, context)));
            if (mode == "after")
            {
                var removeAfterApproval = Command(OperationKinds.RemoveSigner, "remove-after-approval", original.Id,
                    original.AssigneeId);
                removeAfterApproval.Operation!.RelatedId = signer.Id;
                var removedAfterApproval = EngineRuntime.Execute(firstDone.State, removeAfterApproval, context);
                check("后加签未处理时发起人已同意也能减签", removedAfterApproval.State.Status ==
                    EngineInstanceStatuses.Approved);
            }
        }
    }

    /// <summary>
    /// 检查批量同意逐项遵守节点设置且不改写输入。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckBatch(Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        graph.Nodes[1].Approval = new FlowApprovalSettings
        {
            AllowedActions = [EngineCommandKinds.Approve, OperationKinds.BulkApprove],
        };
        var context = Context(graph);
        var first = Start(context);
        var firstTask = first.State.Tasks.Single();
        var item = new BatchApprovalItem
        {
            State = first.State,
            Context = context,
            Command = Command(EngineCommandKinds.Approve, "bulk-1", firstTask.Id, firstTask.AssigneeId),
        };
        var approved = BatchApproval.Execute([item]);
        check("批量同意经统一入口处理且不改输入", approved.Single().State.Status == EngineInstanceStatuses.Approved &&
            first.State.Tasks.Single().Status == EngineTaskStatuses.Pending && !item.Command.Operation!.IsBulkApproval);
        graph.Nodes[1].Approval!.AllowedActions.Remove(OperationKinds.BulkApprove);
        check("节点可以单独禁止批量同意", Throws(ErrorCodes.Conflict, () => BatchApproval.Execute([item])));
        graph.Nodes[1].Approval!.AllowedActions.Add(OperationKinds.BulkApprove);
        check("同一审批单不能在一批里重复处理", Throws(ErrorCodes.ValidationFailed, () =>
            BatchApproval.Execute([item, item])));
    }

    /// <summary>
    /// 检查退回发起人后的两种走法与跨分支限制。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckReturns(Action<string, bool> check)
    {
        var context = Context(EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval));
        var started = Start(context);
        var first = started.State.Tasks.Single();
        var advanced = EngineRuntime.Execute(started.State,
            Command(EngineCommandKinds.Approve, "first", first.Id, first.AssigneeId), context);
        var second = advanced.State.Tasks.Last();
        var returned = EngineRuntime.Execute(advanced.State,
            Command(OperationKinds.Return, "return", second.Id, second.AssigneeId), context);
        var restarted = EngineRuntime.Execute(returned.State,
            Command(OperationKinds.Resubmit, "restart", actor: "initiator"), context);
        check("退回给发起人再提交可从头开始", returned.State.Status == EngineInstanceStatuses.AwaitingInitiator &&
            restarted.State.Tasks.Last().NodeId == "n1" && restarted.State.Status == EngineInstanceStatuses.Running);
        var resumeContext = Context(EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval));
        resumeContext.Operations.ReturnMode = "returnNode";
        var resumeStart = Start(resumeContext);
        var resumeFirst = resumeStart.State.Tasks.Single();
        var resumeAdvanced = EngineRuntime.Execute(resumeStart.State,
            Command(EngineCommandKinds.Approve, "resume-first", resumeFirst.Id, resumeFirst.AssigneeId), resumeContext);
        var resumeSecond = resumeAdvanced.State.Tasks.Last();
        var returnNode = Command(OperationKinds.Return, "return-node", resumeSecond.Id, resumeSecond.AssigneeId);
        var toInitiator = EngineRuntime.Execute(resumeAdvanced.State, returnNode, resumeContext);
        var resumed = EngineRuntime.Execute(toInitiator.State,
            Command(OperationKinds.Resubmit, "resume", actor: "initiator"), resumeContext);
        check("退回给发起人再提交可回到退回节点", resumed.State.Tasks.Last().NodeId == "n2" &&
            resumed.State.Tasks.Last().Status == EngineTaskStatuses.Pending);
        var toPrevious = Command(OperationKinds.Return, "previous", second.Id, second.AssigneeId);
        toPrevious.Operation!.TargetNodeId = "n1";
        var previous = EngineRuntime.Execute(advanced.State, toPrevious, context);
        check("退回到已走过的上一步直接重建任务", previous.State.Tasks.Last().NodeId == "n1" &&
            previous.State.Tasks.Last().Status == EngineTaskStatuses.Pending);

        var graph = EngineScenarioChecks.GatewayGraph(FlowNodeTypes.ParallelGateway);
        var parallelContext = Context(graph);
        var parallel = Start(parallelContext);
        var branchTask = parallel.State.Tasks.Single(item => item.NodeId == "a");
        var crossBranch = Command(OperationKinds.Return, "cross", branchTask.Id, branchTask.AssigneeId);
        crossBranch.Operation!.TargetNodeId = "b";
        check("并行分支不能退回另一分支", Throws(ErrorCodes.Conflict, () =>
            EngineRuntime.Execute(parallel.State, crossBranch, parallelContext)));
    }

    /// <summary>
    /// 检查管理员原因、修改审批人、跳转和发起人撤回限制。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckAdministration(Action<string, bool> check)
    {
        var context = Context(EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval));
        context.Operations.WithdrawBeforeFirstDecision = true;
        var started = Start(context);
        var task = started.State.Tasks.Single();
        var reassign = Command(OperationKinds.Reassign, "reassign", task.Id, "admin", target: "new-user");
        reassign.Operation!.IsAdministrator = true;
        check("管理员修改审批人必须填写原因", Throws(ErrorCodes.ValidationFailed, () =>
            EngineRuntime.Execute(started.State, reassign, context)));
        reassign.Operation.Reason = "原审批人离职";
        var changed = EngineRuntime.Execute(started.State, reassign, context);
        check("管理员修改审批人记录原因和目标", changed.State.Tasks.Single().AssigneeId == "new-user" &&
            changed.Events.Any(item => item.Audit?.Reason == "原审批人离职" && item.Audit.TargetUserId == "new-user"));
        var future = Command(OperationKinds.Reassign, "future", actor: "admin", target: "future-user");
        future.Operation!.IsAdministrator = true;
        future.Operation.Reason = "调整后续审批人";
        future.Operation.TargetNodeId = "n2";
        var configured = EngineRuntime.Execute(started.State, future, context);
        var configuredFirst = configured.State.Tasks.Single();
        var reachedFuture = EngineRuntime.Execute(configured.State,
            Command(EngineCommandKinds.Approve, "reach-future", configuredFirst.Id, configuredFirst.AssigneeId), context);
        check("管理员可提前覆盖后续节点审批人", reachedFuture.State.Tasks.Last().AssigneeId == "future-user");
        check("普通人员不能执行管理员命令", Throws(ErrorCodes.Forbidden, () =>
            EngineRuntime.Execute(started.State,
                Command(EngineCommandKinds.Terminate, "plain", actor: task.AssigneeId), context)));
        var jump = Command(OperationKinds.Jump, "jump", actor: "admin");
        jump.Operation!.IsAdministrator = true;
        jump.Operation.Reason = "纠正路径";
        jump.Operation.TargetNodeId = "n2";
        var jumped = EngineRuntime.Execute(started.State, jump, context);
        check("管理员跳转取消旧待办并建立目标任务", jumped.State.Tasks[0].Status == EngineTaskStatuses.Cancelled &&
            jumped.State.Tasks.Last().NodeId == "n2");
        var firstApproved = EngineRuntime.Execute(started.State,
            Command(EngineCommandKinds.Approve, "approved", task.Id, task.AssigneeId), context);
        check("首位审批人处理后不能撤回", Throws(ErrorCodes.Conflict, () =>
            EngineRuntime.Execute(firstApproved.State,
                Command(EngineCommandKinds.Withdraw, "late", actor: "initiator"), context)));
        check("审批中不能申请撤销", Throws(ErrorCodes.Conflict, () =>
            EngineRuntime.Execute(started.State,
                Command(OperationKinds.RequestCancellation, "cancel-early", actor: "initiator"), context)));
        var finalTask = firstApproved.State.Tasks.Last();
        var approved = EngineRuntime.Execute(firstApproved.State,
            Command(EngineCommandKinds.Approve, "final", finalTask.Id, finalTask.AssigneeId), context);
        var request = EngineRuntime.Execute(approved.State,
            Command(OperationKinds.RequestCancellation, "cancel-request", actor: "initiator"), context);
        check("撤销申请先产生新审批请求且不直接改原审批单", request.State.Status == EngineInstanceStatuses.Approved &&
            request.Events.Any(item => item.Kind == "cancellationApprovalRequested"));
        check("撤销审批未通过时不能直接撤销", Throws(ErrorCodes.Conflict, () =>
            EngineRuntime.Execute(request.State, Command(EngineCommandKinds.Cancel, "cancel", actor: "initiator"), context)));
        context.Operations.CancellationApproved = true;
        var cancelled = EngineRuntime.Execute(request.State,
            Command(EngineCommandKinds.Cancel, "cancel-approved", actor: "initiator"), context);
        check("撤销审批通过后原审批单才变已撤销", cancelled.State.Status == EngineInstanceStatuses.Cancelled &&
            cancelled.Events.Any(item => item.Kind == "instanceCancelled"));
    }

    /// <summary>
    /// 建立含身份与流程编码的运行上下文。
    /// </summary>
    /// <param name="graph">执行图。</param>
    /// <returns>确定性的上下文。</returns>
    private static EngineContext Context(FlowExecutionGraph graph)
    {
        var context = EngineScenarioChecks.Context(graph);
        context.Operations = new OperationContext { InitiatorId = "initiator", FlowCode = "FLOW" };
        return context;
    }

    /// <summary>
    /// 发起审批并保留可持久化的身份信息。
    /// </summary>
    /// <param name="context">运行上下文。</param>
    /// <returns>发起结果。</returns>
    private static EngineResult Start(EngineContext context) => EngineRuntime.Execute(null,
        EngineScenarioChecks.Command(EngineCommandKinds.Start, "start"), context);

    /// <summary>
    /// 生成附带操作人和意见的命令。
    /// </summary>
    /// <param name="kind">命令种类。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="actor">操作人。</param>
    /// <param name="opinion">意见。</param>
    /// <param name="target">目标人员。</param>
    /// <returns>引擎命令。</returns>
    private static EngineCommand Command(string kind, string requestId, string? taskId = null, string? actor = null,
        string? opinion = null, string? target = null) => new()
        {
            Kind = kind,
            RequestId = requestId,
            InstanceId = "instance",
            TaskId = taskId,
            Operation = actor is null && kind == EngineCommandKinds.Approve ? null : new OperationInput
            {
                ActorId = actor ?? string.Empty,
                Opinion = opinion,
                TargetUserId = target,
            },
        };

    /// <summary>
    /// 判断操作是否得到指定稳定错误码。
    /// </summary>
    /// <param name="code">预期错误码。</param>
    /// <param name="action">待执行操作。</param>
    /// <returns>得到预期错误时为真。</returns>
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
