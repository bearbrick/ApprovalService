using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 在唯一命令入口中执行人工扩展操作，所有状态改动均作用于入口创建的副本。
/// </summary>
internal static class OperationDispatcher
{
    /// <summary>
    /// 执行扩展命令并追加确定性事件。
    /// </summary>
    /// <param name="state">本次可修改的状态副本。</param>
    /// <param name="command">命令。</param>
    /// <param name="context">固定输入。</param>
    /// <param name="work">令牌推进与事件计算区。</param>
    public static void Execute(EngineState state, EngineCommand command, EngineContext context, EngineWork work)
    {
        var input = command.Operation ?? throw new EngineException(ErrorCodes.ValidationFailed, "操作参数不能为空。");
        switch (command.Kind)
        {
            case OperationKinds.RequestCancellation:
                if (state.Status != EngineInstanceStatuses.Approved)
                {
                    throw new EngineException(ErrorCodes.Conflict, "只有已通过的审批单可以发起撤销审批。");
                }

                if (state.Operations.CancellationRequested)
                {
                    throw new EngineException(ErrorCodes.Conflict, "该审批单已经申请撤销审批。");
                }

                state.Operations.CancellationRequested = true;
                work.Emit("cancellationApprovalRequested");
                break;
            case OperationKinds.DelegateExisting:
                OperationDelegationResolver.ApplyExisting(state, command, context, work);
                break;
            case OperationKinds.AddSigner:
                OperationSignerCommands.Add(state, command, work);
                break;
            case OperationKinds.RemoveSigner:
                OperationSignerCommands.Remove(state, command, work);
                break;
            case OperationKinds.RevokeApproval:
                OperationUndo.Revoke(state, command, work);
                break;
            case OperationKinds.Transfer:
                RequireRunning(state);
                var transfer = PendingTask(state, command.TaskId);
                RequireTarget(input.TargetUserId);
                if (transfer.AssigneeId == input.TargetUserId)
                {
                    throw new EngineException(ErrorCodes.ValidationFailed, "不能转交给当前审批人。");
                }

                transfer.AssigneeId = input.TargetUserId!;
                transfer.DelegatorId = null;
                work.Emit("taskTransferred", transfer.NodeId, transfer.TokenId, transfer.Id);
                break;
            case OperationKinds.Remind:
                Remind(state, context, work);
                break;
            case OperationKinds.SaveDraft:
                RequireRunning(state);
                var draftTask = PendingTask(state, command.TaskId);
                state.Operations.DraftOpinions[draftTask.Id] = input.Opinion ?? string.Empty;
                work.Emit("opinionDraftSaved", draftTask.NodeId, draftTask.TokenId, draftTask.Id);
                break;
            case OperationKinds.Comment:
                RequireRunning(state);
                RequireOpinion(input);
                var commentTask = PendingTask(state, command.TaskId);
                work.Emit("commentAdded", commentTask.NodeId, commentTask.TokenId, commentTask.Id);
                break;
            case OperationKinds.CopyTo:
                RequireRunning(state);
                RequireTarget(input.TargetUserId);
                var copyTask = PendingTask(state, command.TaskId);
                state.Operations.CopyRecipients.Add(input.TargetUserId!);
                work.Emit("copyRequested", copyTask.NodeId, copyTask.TokenId, copyTask.Id);
                break;
            case OperationKinds.Consult:
                RequireRunning(state);
                RequireTarget(input.TargetUserId);
                var consultationTask = PendingTask(state, command.TaskId);
                state.Operations.Consultations.Add(new OperationConsultation
                {
                    Id = work.NewId("consultation"),
                    TaskId = consultationTask.Id,
                    RecipientId = input.TargetUserId!,
                });
                work.Emit("consultationRequested", consultationTask.NodeId, consultationTask.TokenId, consultationTask.Id);
                work.Events[^1].TargetCode = state.Operations.Consultations[^1].Id;
                break;
            case OperationKinds.Reply:
                Reply(state, command, work);
                break;
            case OperationKinds.RequestSupplement:
                RequestSupplement(state, command, work);
                break;
            case OperationKinds.Supplement:
                Supplement(state, work);
                break;
            case OperationKinds.Return:
                Return(state, command, context, work);
                break;
            case OperationKinds.Resubmit:
                Resubmit(state, command, context, work);
                break;
            case OperationKinds.Reassign:
                RequireRunning(state);
                RequireTarget(input.TargetUserId);
                if (command.TaskId is null)
                {
                    var future = context.Graph.Nodes.SingleOrDefault(item => item.Id == input.TargetNodeId);
                    if (future?.Type is not (FlowNodeTypes.Approval or FlowNodeTypes.Handling or FlowNodeTypes.Cc))
                    {
                        throw new EngineException(ErrorCodes.ValidationFailed, "未来审批人只能设置在人工节点。");
                    }

                    state.Operations.AssigneeOverrides[future.Id] = input.TargetUserId!;
                    work.Emit("futureAssigneeChanged", future.Id);
                    break;
                }

                var reassign = state.Tasks.SingleOrDefault(item => item.Id == command.TaskId &&
                    item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued)
                    ?? throw new EngineException(ErrorCodes.Conflict, "任务不存在或已处理。");
                reassign.AssigneeId = input.TargetUserId!;
                reassign.DelegatorId = null;
                work.Emit("taskReassigned", reassign.NodeId, reassign.TokenId, reassign.Id);
                break;
            case OperationKinds.Handover:
                Handover(state, command, work);
                break;
            case OperationKinds.ReassignInactive:
                InactiveAssigneeOperation.Reassign(state, command, context, work);
                break;
            case OperationKinds.Jump:
                Jump(state, command, context, work);
                break;
            case OperationKinds.Resend:
                if (input.Mode is not ("reminder" or "callback"))
                {
                    throw new EngineException(ErrorCodes.ValidationFailed, "只能重发提醒或回调。");
                }

                work.Emit("outboundResendRequested", targetCode: input.Mode);
                break;
            default:
                throw new EngineException(ErrorCodes.ValidationFailed, "不支持的引擎命令。");
        }
    }

    /// <summary>
    /// 按节点限制催办频率。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="context">固定时间。</param>
    /// <param name="work">事件计算区。</param>
    private static void Remind(EngineState state, EngineContext context, EngineWork work)
    {
        RequireRunning(state);
        var active = state.Tasks.Where(item => item.Status == EngineTaskStatuses.Pending).ToArray();
        if (active.Length == 0)
        {
            throw new EngineException(ErrorCodes.Conflict, "当前没有可催办的审批任务。");
        }

        foreach (var nodeId in active.Select(item => item.NodeId).Distinct(StringComparer.Ordinal))
        {
            if (state.Operations.LastReminders.TryGetValue(nodeId, out var last) &&
                context.OccurredAt - last < TimeSpan.FromHours(1))
            {
                throw new EngineException(ErrorCodes.Conflict, "同一步每小时最多催办一次。");
            }

            state.Operations.LastReminders[nodeId] = context.OccurredAt;
            work.Emit("reminderRequested", nodeId);
        }
    }

    /// <summary>
    /// 保存征询回复，不改变审批流向。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="command">回复命令。</param>
    /// <param name="work">事件计算区。</param>
    private static void Reply(EngineState state, EngineCommand command, EngineWork work)
    {
        RequireRunning(state);
        var input = command.Operation!;
        RequireOpinion(input);
        var consultation = state.Operations.Consultations.SingleOrDefault(item => item.Id == input.RelatedId && !item.Replied)
            ?? throw new EngineException(ErrorCodes.Conflict, "征询不存在或已经回复。");
        if (command.TaskId != consultation.TaskId)
        {
            throw new EngineException(ErrorCodes.Conflict, "征询回复必须使用原发问任务。");
        }

        // 即使其他分支仍在运行，已完成的原任务也不能追加迟到回复。
        var task = PendingTask(state, consultation.TaskId);

        if (input.FieldValues is not null)
        {
            // 受询人没有原审批任务的字段编辑权限，回复只能携带意见。
            throw new EngineException(ErrorCodes.Forbidden, "征询回复不能填写审批人字段。");
        }

        if (consultation.RecipientId != input.ActorId)
        {
            throw new EngineException(ErrorCodes.Forbidden, "只有受询人可以回复。");
        }

        consultation.Replied = true;
        work.Emit("consultationReplied", task.NodeId, task.TokenId, task.Id, consultation.Id);
    }

    /// <summary>
    /// 暂停当前任务，等待业务系统送来补充数据。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="command">命令。</param>
    /// <param name="work">事件计算区。</param>
    private static void RequestSupplement(EngineState state, EngineCommand command, EngineWork work)
    {
        RequireRunning(state);
        var task = PendingTask(state, command.TaskId);
        if (state.Operations.Pause is not null)
        {
            throw new EngineException(ErrorCodes.Conflict, "审批单已经在等待发起人处理。");
        }

        state.Operations.Pause = new OperationPause { Kind = "supplement", NodeId = task.NodeId, TokenId = task.TokenId };
        state.Status = EngineInstanceStatuses.AwaitingInitiator;
        work.Emit("supplementRequested", task.NodeId, task.TokenId, task.Id);
    }

    /// <summary>
    /// 补充后恢复原任务，而不重走审批链。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="work">事件计算区。</param>
    private static void Supplement(EngineState state, EngineWork work)
    {
        var pause = state.Operations.Pause;
        if (state.Status != EngineInstanceStatuses.AwaitingInitiator || pause?.Kind != "supplement")
        {
            throw new EngineException(ErrorCodes.Conflict, "当前没有待补充的审批任务。");
        }

        state.Status = EngineInstanceStatuses.Running;
        state.Operations.Pause = null;
        work.Emit("supplementSubmitted", pause.NodeId, pause.TokenId);
    }

    /// <summary>
    /// 退回给发起人或同分支的历史节点。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="command">退回命令。</param>
    /// <param name="context">运行图。</param>
    /// <param name="work">事件计算区。</param>
    private static void Return(EngineState state, EngineCommand command, EngineContext context, EngineWork work)
    {
        RequireRunning(state);
        var task = PendingTask(state, command.TaskId);
        var token = state.Tokens.Single(item => item.Id == task.TokenId);
        var input = command.Operation!;
        if (input.TargetNodeId is null)
        {
            var mode = state.Operations.ReturnMode;
            if (mode is not ("restart" or "returnNode"))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "未知的重新提交方式。");
            }

            state.Operations.Pause = new OperationPause
            {
                Kind = "return",
                NodeId = task.NodeId,
                TokenId = token.Id,
                Mode = mode,
                Forks = OperationStateCopy.CopyForks(token.Forks),
            };
            task.Status = EngineTaskStatuses.Returned;
            token.WaitingFor = "resubmit";
            state.Status = EngineInstanceStatuses.AwaitingInitiator;
            work.Emit("returnedToInitiator", task.NodeId, token.Id, task.Id);
            return;
        }

        if (input.TargetNodeId == task.NodeId)
        {
            throw new EngineException(ErrorCodes.Conflict, "退回目标必须是之前处理过的其他节点。");
        }

        var target = state.Operations.Visits.LastOrDefault(item => item.NodeId == input.TargetNodeId &&
            IsBranchPrefix(item.Forks, token.Forks));
        if (target is null || !context.Graph.Nodes.Any(item => item.Id == target.NodeId))
        {
            throw new EngineException(ErrorCodes.Conflict, "退回目标不在当前分支或分叉之前。");
        }

        foreach (var oldToken in state.Tokens.Where(item => IsBranchPrefix(target.Forks, item.Forks)).ToArray())
        {
            state.Tokens.Remove(oldToken);
            foreach (var oldTask in state.Tasks.Where(item => item.TokenId == oldToken.Id &&
                item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued))
            {
                oldTask.Status = oldTask.Id == task.Id ? EngineTaskStatuses.Returned : EngineTaskStatuses.Cancelled;
            }
        }

        var resumed = new EngineToken
        {
            Id = work.NewId("token"),
            NodeId = target.NodeId,
            Forks = OperationStateCopy.CopyForks(target.Forks),
        };
        state.Tokens.Add(resumed);
        work.Emit("returnedToNode", target.NodeId, resumed.Id, task.Id);
        work.Advance();
    }

    /// <summary>
    /// 判断目标路径是否是当前路径的同分支前缀。
    /// </summary>
    /// <param name="prefix">目标节点的分叉路径。</param>
    /// <param name="current">当前分叉路径。</param>
    /// <returns>可从当前分支退回时为真。</returns>
    private static bool IsBranchPrefix(IReadOnlyList<EngineForkFrame> prefix, IReadOnlyList<EngineForkFrame> current) =>
        prefix.Count <= current.Count && prefix.Select((frame, index) =>
            frame.ForkId == current[index].ForkId && frame.BranchEdgeId == current[index].BranchEdgeId).All(value => value);

    /// <summary>
    /// 发起人重新提交，按配置从头或从退回节点恢复。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="command">重新提交命令。</param>
    /// <param name="context">运行图。</param>
    /// <param name="work">事件计算区。</param>
    private static void Resubmit(EngineState state, EngineCommand command, EngineContext context, EngineWork work)
    {
        var pause = state.Operations.Pause;
        if (state.Status != EngineInstanceStatuses.AwaitingInitiator || pause?.Kind != "return")
        {
            throw new EngineException(ErrorCodes.Conflict, "当前没有待重新提交的退回审批。");
        }

        state.Status = EngineInstanceStatuses.Running;
        state.Operations.Pause = null;
        if (pause.Mode == "restart")
        {
            foreach (var task in state.Tasks.Where(item => item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued))
            {
                task.Status = EngineTaskStatuses.Cancelled;
            }

            state.Tokens.Clear();
            var start = context.Graph.Nodes.Single(item => item.Type == FlowNodeTypes.Start);
            state.Tokens.Add(new EngineToken { Id = work.NewId("token"), NodeId = start.Id });
        }
        else
        {
            var token = state.Tokens.Single(item => item.Id == pause.TokenId);
            token.NodeId = pause.NodeId;
            token.WaitingFor = null;
            token.Forks = OperationStateCopy.CopyForks(pause.Forks);
        }

        work.Emit("instanceResubmitted", pause.NodeId, pause.TokenId);
        work.Advance();
    }

    /// <summary>
    /// 管理员交接当前在途任务。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="command">交接命令。</param>
    /// <param name="work">事件计算区。</param>
    private static void Handover(EngineState state, EngineCommand command, EngineWork work)
    {
        RequireRunning(state);
        var input = command.Operation!;
        RequireTarget(input.RelatedId);
        RequireTarget(input.TargetUserId);
        if (input.RelatedId == input.TargetUserId)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "交接的原人员和接手人员不能相同。");
        }

        var tasks = state.Tasks.Where(item => item.AssigneeId == input.RelatedId &&
            item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued).ToArray();
        if (tasks.Length == 0)
        {
            throw new EngineException(ErrorCodes.Conflict, "没有可交接的在途任务。");
        }

        foreach (var task in tasks)
        {
            task.AssigneeId = input.TargetUserId!;
            task.DelegatorId = null;
            work.Emit("taskHandedOver", task.NodeId, task.TokenId, task.Id);
        }
    }

    /// <summary>
    /// 管理员跳转到可执行节点，并取消原在途任务。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="command">跳转命令。</param>
    /// <param name="context">运行图。</param>
    /// <param name="work">事件计算区。</param>
    private static void Jump(EngineState state, EngineCommand command, EngineContext context, EngineWork work)
    {
        RequireRunning(state);
        var targetId = command.Operation!.TargetNodeId;
        var target = context.Graph.Nodes.SingleOrDefault(item => item.Id == targetId)
            ?? throw new EngineException(ErrorCodes.ValidationFailed, "跳转目标节点不存在。");
        if (target.GatewayDirection == FlowGatewayDirections.Join)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "不能直接跳到汇合网关。");
        }

        foreach (var task in state.Tasks.Where(item => item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued))
        {
            task.Status = EngineTaskStatuses.Cancelled;
        }

        state.Tokens.Clear();
        state.Tokens.Add(new EngineToken { Id = work.NewId("token"), NodeId = target.Id });
        work.Emit("instanceJumped", target.Id);
        work.Advance();
    }

    /// <summary>
    /// 找到待办任务。
    /// </summary>
    /// <param name="state">审批状态。</param>
    /// <param name="taskId">任务 ID。</param>
    /// <returns>待办任务。</returns>
    private static EngineTask PendingTask(EngineState state, string? taskId) =>
        state.Tasks.SingleOrDefault(item => item.Id == taskId && item.Status == EngineTaskStatuses.Pending)
            ?? throw new EngineException(ErrorCodes.Conflict, "任务不存在或已处理。");

    /// <summary>
    /// 要求审批单仍在审批中。
    /// </summary>
    /// <param name="state">审批状态。</param>
    private static void RequireRunning(EngineState state)
    {
        if (state.Status != EngineInstanceStatuses.Running)
        {
            throw new EngineException(ErrorCodes.Conflict, "审批单不在审批中。");
        }
    }

    /// <summary>
    /// 要求非空目标值。
    /// </summary>
    /// <param name="value">目标值。</param>
    private static void RequireTarget(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "操作目标不能为空。");
        }
    }

    /// <summary>
    /// 要求输入非空意见。
    /// </summary>
    /// <param name="input">操作输入。</param>
    private static void RequireOpinion(OperationInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Opinion))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "意见不能为空。");
        }
    }
}
