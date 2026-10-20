using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 在下一位审批人处理前保存并恢复同意前的纯状态。
/// </summary>
internal static class OperationUndo
{
    /// <summary>
    /// 捕获同意命令前的状态，故意清除旧撤销帧，避免递归快照。
    /// </summary>
    /// <param name="state">尚未处理同意的状态。</param>
    /// <param name="command">同意命令。</param>
    /// <returns>可在本次同意后写入的新撤销帧。</returns>
    public static OperationUndoFrame Capture(EngineState state, EngineCommand command)
    {
        var snapshot = EngineStateCopy.Copy(state);
        snapshot.Operations.LastApprovalUndo = null;
        return new OperationUndoFrame
        {
            Snapshot = snapshot,
            ActorId = command.Operation!.ActorId,
            TaskId = command.TaskId!,
            DecisionCountAfter = state.Operations.HumanDecisionCount + 1,
            ProcessedRequestCountAfter = state.ProcessedRequestIds.Count + 1,
        };
    }

    /// <summary>
    /// 收回本人的最近一次同意并以事件记录这次逆转。
    /// </summary>
    /// <param name="state">当前状态副本。</param>
    /// <param name="command">撤销同意命令。</param>
    /// <param name="work">事件计算区。</param>
    public static void Revoke(EngineState state, EngineCommand command, EngineWork work)
    {
        var frame = state.Operations.LastApprovalUndo;
        if (state.Status != EngineInstanceStatuses.Running || frame is null ||
            frame.TaskId != command.TaskId || frame.ActorId != command.Operation!.ActorId ||
            state.Operations.HumanDecisionCount != frame.DecisionCountAfter ||
            state.ProcessedRequestIds.Count != frame.ProcessedRequestCountAfter)
        {
            throw new EngineException(ErrorCodes.Conflict, "下一步已有操作，或当前人员不能撤销这次同意。");
        }

        var restored = EngineStateCopy.Copy(frame.Snapshot);
        var oldTaskIds = restored.Tasks.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var later in state.Tasks.Where(item => !oldTaskIds.Contains(item.Id) &&
            item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued))
        {
            work.Emit("taskCancelled", later.NodeId, later.TokenId, later.Id);
        }

        state.Status = restored.Status;
        state.StepCount = restored.StepCount;
        state.Tokens = restored.Tokens;
        state.Tasks = restored.Tasks;
        state.ApprovedAssigneeIds = restored.ApprovedAssigneeIds;
        state.LastApprovedAssigneeId = restored.LastApprovedAssigneeId;
        state.Operations = restored.Operations;
        work.Emit("approvalRevoked", taskId: frame.TaskId);
    }
}
