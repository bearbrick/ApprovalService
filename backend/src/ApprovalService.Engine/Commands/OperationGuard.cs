using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 统一命令入口的身份、节点配置和意见校验。
/// </summary>
internal static class OperationGuard
{
    /// <summary>
    /// 在状态副本变更前检查人工和管理命令。
    /// </summary>
    /// <param name="state">当前状态副本。</param>
    /// <param name="command">待执行命令。</param>
    /// <param name="context">已发布流程及固定上下文。</param>
    /// <returns>相关任务；实例级命令返回空。</returns>
    public static EngineTask? Validate(EngineState state, EngineCommand command, EngineContext context)
    {
        var input = command.Operation;
        if (input is null)
        {
            // 新建审批单一旦绑定发起人，所有人工操作都必须显式传入认证后的身份。
            if (!string.IsNullOrWhiteSpace(state.Operations.InitiatorId) &&
                command.Kind is not (EngineCommandKinds.ActionSucceeded or EngineCommandKinds.ActionFailed or
                    EngineCommandKinds.SubprocessCompleted))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "操作人不能为空。");
            }

            // 兼容没有身份信息的既有纯引擎场景。
            return null;
        }

        if (string.IsNullOrWhiteSpace(input.ActorId))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "操作人不能为空。");
        }

        if (command.Kind == OperationKinds.RevokeApproval)
        {
            var approved = state.Tasks.SingleOrDefault(item => item.Id == command.TaskId &&
                item.Status == EngineTaskStatuses.Approved);
            return approved is null ? null : new EngineTask
            {
                Id = approved.Id,
                AssigneeId = approved.AssigneeId,
                DelegatorId = approved.DelegatorId,
            };
        }

        if (command.Kind is EngineCommandKinds.Approve or EngineCommandKinds.Reject or
            EngineCommandKinds.Handle or EngineCommandKinds.Acknowledge || IsTaskOperation(command.Kind))
        {
            var task = state.Tasks.SingleOrDefault(item => item.Id == command.TaskId &&
                (item.Status == EngineTaskStatuses.Pending ||
                    (command.Kind == OperationKinds.RemoveSigner &&
                        item.Status is EngineTaskStatuses.Queued or EngineTaskStatuses.Approved) ||
                    (command.Kind == OperationKinds.DelegateExisting && item.Status == EngineTaskStatuses.Queued)))
                ?? throw new EngineException(ErrorCodes.Conflict, "任务不存在或已处理。");
            var administratorDelegation = command.Kind == OperationKinds.DelegateExisting && input.IsAdministrator;
            if (!administratorDelegation && input.ActorId != task.AssigneeId && input.ActorId != task.DelegatorId)
            {
                throw new EngineException(ErrorCodes.Forbidden, "当前人员不能处理该任务。");
            }

            if (administratorDelegation && string.IsNullOrWhiteSpace(input.Reason))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "管理员转委托已有任务必须填写原因。");
            }

            var node = context.Graph.Nodes.Single(item => item.Id == task.NodeId);
            var allowed = node.Approval?.AllowedActions;
            if (command.Kind != OperationKinds.DelegateExisting && allowed is { Count: > 0 } &&
                !allowed.Contains(command.Kind, StringComparer.Ordinal))
            {
                throw new EngineException(ErrorCodes.Conflict, "当前节点不允许此操作。");
            }

            if (input.IsBulkApproval && allowed is { Count: > 0 } &&
                !allowed.Contains(OperationKinds.BulkApprove, StringComparer.Ordinal))
            {
                throw new EngineException(ErrorCodes.Conflict, "当前节点不允许批量同意。");
            }

            if (input.IsBulkApproval && node.Approval?.SignatureRequired == true)
            {
                throw new EngineException(ErrorCodes.Conflict, "此节点需逐项明确选择手写签名，不能批量同意。");
            }

            if (node.Approval?.CommentRequired == true && IsDecision(command.Kind) &&
                string.IsNullOrWhiteSpace(input.Opinion))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "当前节点必须填写审批意见。");
            }

            if (node.Approval?.SignatureRequired == true &&
                command.Kind is EngineCommandKinds.Approve or EngineCommandKinds.Handle or EngineCommandKinds.Acknowledge &&
                string.IsNullOrWhiteSpace(input.SignatureFileId))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "当前节点成功办理时必须明确选择手写签名。");
            }

            return new EngineTask { Id = task.Id, AssigneeId = task.AssigneeId, DelegatorId = task.DelegatorId };
        }

        if (command.Kind is EngineCommandKinds.Withdraw or EngineCommandKinds.Cancel or OperationKinds.RequestCancellation or OperationKinds.Remind or
            OperationKinds.Resubmit or OperationKinds.Supplement)
        {
            if (input.ActorId != state.Operations.InitiatorId)
            {
                throw new EngineException(ErrorCodes.Forbidden, "只有发起人可以执行此操作。");
            }

            if (command.Kind == EngineCommandKinds.Withdraw && state.Operations.WithdrawBeforeFirstDecision &&
                state.Operations.HasHumanDecision)
            {
                throw new EngineException(ErrorCodes.Conflict, "首位审批人已处理，不能撤回。");
            }

            if (command.Kind == EngineCommandKinds.Cancel && !context.Operations.CancellationApproved)
            {
                throw new EngineException(ErrorCodes.Conflict, "撤销审批尚未通过，不能撤销原审批单。");
            }
        }

        if (command.Kind == EngineCommandKinds.Terminate)
        {
            if (!input.IsAdministrator && !input.IsOwningClientApp)
            {
                throw new EngineException(ErrorCodes.Forbidden, "只有管理员或审批单所属应用可以终止审批。");
            }

            if (string.IsNullOrWhiteSpace(input.Reason))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "终止审批必须填写原因。");
            }
        }

        if (command.Kind == OperationKinds.UpdateData)
        {
            if (!input.IsOwningClientApp)
            {
                throw new EngineException(ErrorCodes.Forbidden, "只有审批单所属接入应用可以更新审批数据。");
            }

            if (input.DataChanges?.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "审批数据更新缺少改前改后审计值。");
            }
        }

        if (command.Kind is OperationKinds.Reassign or OperationKinds.Jump or
            OperationKinds.Handover or OperationKinds.ReassignInactive or OperationKinds.Resend)
        {
            if (!input.IsAdministrator)
            {
                throw new EngineException(ErrorCodes.Forbidden, "只有管理员可以干预审批。");
            }

            if (string.IsNullOrWhiteSpace(input.Reason))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "管理员干预必须填写原因。");
            }
        }

        return null;
    }

    /// <summary>
    /// 判断是否为当前任务上的扩展操作。
    /// </summary>
    /// <param name="kind">命令编码。</param>
    /// <returns>属于任务操作时为真。</returns>
    private static bool IsTaskOperation(string kind) => kind is OperationKinds.Return or OperationKinds.Transfer or
        OperationKinds.Comment or OperationKinds.SaveDraft or OperationKinds.CopyTo or OperationKinds.Consult or
        OperationKinds.RequestSupplement or OperationKinds.AddSigner or OperationKinds.RemoveSigner or
        OperationKinds.DelegateExisting;

    /// <summary>
    /// 判断节点意见必填规则是否适用。
    /// </summary>
    /// <param name="kind">命令编码。</param>
    /// <returns>会完成或中断审批时为真。</returns>
    private static bool IsDecision(string kind) => kind is EngineCommandKinds.Approve or EngineCommandKinds.Reject or
        EngineCommandKinds.Handle or OperationKinds.Return or OperationKinds.RequestSupplement;

    /// <summary>
    /// 为本命令产生的全部事件附上留痕信息。
    /// </summary>
    /// <param name="events">本命令产生的事件。</param>
    /// <param name="command">本命令。</param>
    /// <param name="task">当前处理的任务，可为空。</param>
    public static void AttachAudit(IEnumerable<EngineEvent> events, EngineCommand command, EngineTask? task)
    {
        var input = command.Operation;
        if (input is null)
        {
            return;
        }

        foreach (var item in events)
        {
            item.Audit = new OperationAudit
            {
                ActorId = input.ActorId,
                OnBehalfOfId = input.ActorId == task?.AssigneeId ? task.DelegatorId : null,
                Opinion = input.Opinion,
                SignatureFileId = input.SignatureFileId,
                Reason = input.Reason,
                TargetUserId = input.TargetUserId ??
                    (command.Kind == OperationKinds.ReassignInactive ? item.TargetCode : null),
                MentionedUserIds = [.. input.MentionedUserIds],
                FieldValues = input.FieldValues?.Clone(),
                DataChanges = input.DataChanges?.Clone(),
            };
        }
    }
}
