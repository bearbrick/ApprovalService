using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Commands;

namespace ApprovalService.Engine.Execution;

/// <summary>
/// 审批执行的唯一命令入口。输入状态从不原地修改，所有外部动作只产生事件。
/// </summary>
public static class EngineRuntime
{
    /// <summary>
    /// 用一份执行图、固定时间及已解析上下文计算新状态与事件。
    /// </summary>
    /// <param name="current">当前状态；开始命令时为空。</param>
    /// <param name="command">要处理的命令。</param>
    /// <param name="context">调用方提供的执行图、条件和人员快照。</param>
    /// <returns>新状态及本命令的事件。</returns>
    public static EngineResult Execute(EngineState? current, EngineCommand command, EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(command.RequestId) || string.IsNullOrWhiteSpace(command.InstanceId))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "请求 ID 和审批单 ID 不能为空。");
        }

        if (current is null && command.Kind != EngineCommandKinds.Start)
        {
            throw new EngineException(ErrorCodes.Conflict, "审批单尚未开始。");
        }

        if (current is not null && current.InstanceId != command.InstanceId)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "命令的审批单 ID 与当前状态不一致。");
        }

        if (current?.ProcessedRequestIds.Contains(command.RequestId) == true)
        {
            return new EngineResult { State = EngineStateCopy.Copy(current), IsReplay = true };
        }

        var state = current is null ? new EngineState { InstanceId = command.InstanceId } : EngineStateCopy.Copy(current);
        var work = new EngineWork(state, command, context);
        if (command.Kind == EngineCommandKinds.Start)
        {
            if (current is not null)
            {
                throw new EngineException(ErrorCodes.Conflict, "审批单已经开始。");
            }

            var start = context.Graph.Nodes.SingleOrDefault(node => node.Type == FlowNodeTypes.Start)
                ?? throw new EngineException(ErrorCodes.ValidationFailed, "执行图缺少唯一开始节点。");
            state.Operations.InitiatorId = context.Operations.InitiatorId;
            state.Operations.FlowCode = context.Operations.FlowCode;
            state.Operations.WithdrawBeforeFirstDecision = context.Operations.WithdrawBeforeFirstDecision;
            state.Operations.ReturnMode = context.Operations.ReturnMode;
            state.Tokens.Add(new EngineToken { Id = work.NewId("token"), NodeId = start.Id });
            work.Emit("instanceStarted", start.Id);
            work.Advance();
        }
        else if (command.Kind is EngineCommandKinds.Withdraw or EngineCommandKinds.Terminate or EngineCommandKinds.Cancel)
        {
            OperationGuard.Validate(state, command, context);
            if (command.Kind == EngineCommandKinds.Cancel)
            {
                if (state.Status != EngineInstanceStatuses.Approved)
                {
                    throw new EngineException(ErrorCodes.Conflict, "只有已通过的审批单可以撤销。");
                }

                state.Status = EngineInstanceStatuses.Cancelled;
                work.Emit("instanceCancelled");
                state.ProcessedRequestIds.Add(command.RequestId);
                OperationGuard.AttachAudit(work.Events, command, null);
                return new EngineResult { State = state, Events = work.Events };
            }

            RequireRunning(state);
            state.Status = command.Kind switch
            {
                EngineCommandKinds.Withdraw => EngineInstanceStatuses.Withdrawn,
                _ => EngineInstanceStatuses.Terminated,
            };
            foreach (var task in state.Tasks.Where(item => item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued))
            {
                task.Status = EngineTaskStatuses.Cancelled;
            }

            state.Tokens.Clear();
            work.Emit("instance" + command.Kind[..1].ToUpperInvariant() + command.Kind[1..]);
        }
        else if (command.Kind is EngineCommandKinds.Approve or EngineCommandKinds.Handle or
            EngineCommandKinds.Acknowledge or EngineCommandKinds.Reject)
        {
            RequireRunning(state);
            var auditTask = OperationGuard.Validate(state, command, context);
            var task = state.Tasks.SingleOrDefault(item => item.Id == command.TaskId && item.Status == EngineTaskStatuses.Pending)
                ?? throw new EngineException(ErrorCodes.Conflict, "任务不存在或已处理。");
            var token = state.Tokens.SingleOrDefault(item => item.Id == task.TokenId && item.WaitingFor == "task")
                ?? throw new EngineException(ErrorCodes.Conflict, "任务的执行令牌已失效。");
            var node = context.Graph.Nodes.Single(item => item.Id == task.NodeId);
            if (command.Kind != task.CompletionKind && !(task.CanReject && command.Kind == EngineCommandKinds.Reject))
            {
                throw new EngineException(ErrorCodes.Conflict, "操作与任务节点类型不匹配。");
            }

            var undo = command.Kind == EngineCommandKinds.Approve && command.Operation is not null
                ? OperationUndo.Capture(state, command) : null;
            work.CompleteTask(task, token, node, command.Kind);
            if (undo is not null && state.Status == EngineInstanceStatuses.Running)
            {
                state.Operations.LastApprovalUndo = undo;
            }
            else if (command.Kind is EngineCommandKinds.Approve or EngineCommandKinds.Handle or EngineCommandKinds.Reject)
            {
                state.Operations.LastApprovalUndo = null;
            }
            OperationGuard.AttachAudit(work.Events, command, auditTask);
        }
        else if (command.Kind is EngineCommandKinds.ActionSucceeded or EngineCommandKinds.ActionFailed or
            EngineCommandKinds.SubprocessCompleted)
        {
            RequireRunning(state);
            var token = state.Tokens.SingleOrDefault(item => item.Id == command.TokenId)
                ?? throw new EngineException(ErrorCodes.Conflict, "等待中的执行令牌不存在。");
            var expected = command.Kind == EngineCommandKinds.SubprocessCompleted ? "subprocess" : "action";
            if (token.WaitingFor != expected)
            {
                throw new EngineException(ErrorCodes.Conflict, "令牌没有等待这一类结果。");
            }

            if (command.Kind == EngineCommandKinds.ActionFailed || !command.Success)
            {
                work.Fault("externalActionFailed", token);
            }
            else
            {
                work.Emit(expected == "action" ? "actionCompleted" : "subprocessCompleted", token.NodeId, token.Id);
                token.WaitingFor = null;
                work.MoveSingle(token);
                work.Advance();
            }
        }
        else
        {
            var auditTask = OperationGuard.Validate(state, command, context);
            OperationDispatcher.Execute(state, command, context, work);
            OperationGuard.AttachAudit(work.Events, command, auditTask);
        }

        if (command.Kind is EngineCommandKinds.Withdraw or EngineCommandKinds.Terminate or EngineCommandKinds.Cancel or
            EngineCommandKinds.Start)
        {
            OperationGuard.AttachAudit(work.Events, command, null);
        }

        state.ProcessedRequestIds.Add(command.RequestId);
        return new EngineResult { State = state, Events = work.Events };
    }

    /// <summary>
    /// 仅运行中的审批单可接受处理命令。
    /// </summary>
    /// <param name="state">当前状态。</param>
    private static void RequireRunning(EngineState state)
    {
        if (state.Status != EngineInstanceStatuses.Running)
        {
            throw new EngineException(ErrorCodes.Conflict, "审批单不在审批中。");
        }
    }
}
