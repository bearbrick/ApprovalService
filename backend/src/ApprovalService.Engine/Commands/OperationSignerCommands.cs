using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;
using ApprovalService.Engine.Policies;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 动态加签和减签对同一令牌的任务序列进行修改。
/// </summary>
internal static class OperationSignerCommands
{
    /// <summary>
    /// 确保动态加签人与原审批人都完成后才按节点策略推进。
    /// </summary>
    /// <param name="state">当前状态。</param>
    /// <param name="tokenId">当前令牌。</param>
    /// <param name="decision">节点原多人策略的决定。</param>
    /// <returns>考虑加签关系后的决定。</returns>
    public static ApprovalPolicyDecision AdjustDecision(EngineState state, string tokenId, ApprovalPolicyDecision decision)
    {
        var signers = state.Tasks.Where(item => item.TokenId == tokenId && item.AddedByTaskId is not null &&
            item.Status != EngineTaskStatuses.Cancelled).ToArray();
        if (signers.Length == 0 || decision.Outcome == "rejected")
        {
            return decision;
        }

        if (signers.Any(item => item.Status == EngineTaskStatuses.Rejected))
        {
            return new ApprovalPolicyDecision { Outcome = "rejected" };
        }

        var incomplete = signers.Any(item => item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued ||
            state.Tasks.Any(source => source.Id == item.AddedByTaskId &&
                source.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued));
        if (!incomplete)
        {
            return decision;
        }

        var hasPending = state.Tasks.Any(item => item.TokenId == tokenId && item.Status == EngineTaskStatuses.Pending);
        var queued = state.Tasks.FirstOrDefault(item => item.TokenId == tokenId && item.Status == EngineTaskStatuses.Queued);
        return new ApprovalPolicyDecision
        {
            Outcome = "pending",
            ActivateTaskId = hasPending ? null : queued?.Id,
        };
    }

    /// <summary>
    /// 在当前审批人之前、之后或并行插入一项加签任务。
    /// </summary>
    /// <param name="state">审批状态副本。</param>
    /// <param name="command">加签命令。</param>
    /// <param name="work">事件计算区。</param>
    public static void Add(EngineState state, EngineCommand command, EngineWork work)
    {
        if (state.Status != EngineInstanceStatuses.Running)
        {
            throw new EngineException(ErrorCodes.Conflict, "审批单不在审批中。");
        }

        var source = state.Tasks.SingleOrDefault(item => item.Id == command.TaskId && item.Status == EngineTaskStatuses.Pending)
            ?? throw new EngineException(ErrorCodes.Conflict, "当前任务不存在或已处理。");
        var input = command.Operation!;
        if (string.IsNullOrWhiteSpace(input.TargetUserId) || input.TargetUserId == source.AssigneeId)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "加签人必须是另一名有效人员。");
        }

        var mode = input.Mode ?? "after";
        if (mode is not ("before" or "after" or "parallel"))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "加签位置必须是前、后或并行。");
        }

        var signer = new EngineTask
        {
            Id = work.NewId("task"),
            TokenId = source.TokenId,
            NodeId = source.NodeId,
            AssigneeId = input.TargetUserId,
            CompletionKind = source.CompletionKind,
            CanReject = source.CanReject,
            AddedByTaskId = source.Id,
            SignerMode = mode,
            Status = mode == "after" ? EngineTaskStatuses.Queued : EngineTaskStatuses.Pending,
        };
        var insertionIndex = state.Tasks.IndexOf(source) + (mode == "before" ? 0 : 1);
        state.Tasks.Insert(insertionIndex, signer);
        if (mode == "before")
        {
            source.Status = EngineTaskStatuses.Queued;
        }

        work.Emit("signerAdded", source.NodeId, source.TokenId, signer.Id);
    }

    /// <summary>
    /// 由加签发起人撤销尚未处理的加签任务。
    /// </summary>
    /// <param name="state">审批状态副本。</param>
    /// <param name="command">减签命令。</param>
    /// <param name="work">事件计算区。</param>
    public static void Remove(EngineState state, EngineCommand command, EngineWork work)
    {
        if (state.Status != EngineInstanceStatuses.Running)
        {
            throw new EngineException(ErrorCodes.Conflict, "审批单不在审批中。");
        }

        var source = state.Tasks.SingleOrDefault(item => item.Id == command.TaskId &&
            item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued or EngineTaskStatuses.Approved)
            ?? throw new EngineException(ErrorCodes.Conflict, "加签发起任务已处理。");
        var signer = state.Tasks.SingleOrDefault(item => item.Id == command.Operation!.RelatedId &&
            item.AddedByTaskId == source.Id && item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued)
            ?? throw new EngineException(ErrorCodes.Conflict, "加签任务不存在或对方已经处理。");
        signer.Status = EngineTaskStatuses.Cancelled;
        if (source.Status == EngineTaskStatuses.Queued && signer.SignerMode == "before" &&
            !state.Tasks.Any(item => item.AddedByTaskId == source.Id && item.SignerMode == "before" &&
                item.Status == EngineTaskStatuses.Pending))
        {
            source.Status = EngineTaskStatuses.Pending;
            work.Emit("taskActivated", source.NodeId, source.TokenId, source.Id);
        }

        work.Emit("signerRemoved", signer.NodeId, signer.TokenId, signer.Id);
        if (source.Status == EngineTaskStatuses.Approved &&
            !state.Tasks.Any(item => item.TokenId == source.TokenId && item.Status == EngineTaskStatuses.Pending))
        {
            var queued = state.Tasks.FirstOrDefault(item => item.TokenId == source.TokenId &&
                item.Status == EngineTaskStatuses.Queued);
            if (queued is not null)
            {
                queued.Status = EngineTaskStatuses.Pending;
                work.Emit("taskActivated", queued.NodeId, queued.TokenId, queued.Id);
            }
            else
            {
                var token = state.Tokens.Single(item => item.Id == source.TokenId);
                token.WaitingFor = null;
                work.MoveSingle(token);
                work.Advance();
            }
        }
    }
}
