using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 深拷贝操作快照，防止失败命令污染调用方状态。
/// </summary>
internal static class OperationStateCopy
{
    /// <summary>
    /// 拷贝操作状态及其所有可变集合。
    /// </summary>
    /// <param name="source">原始操作状态。</param>
    /// <returns>独立的操作状态。</returns>
    public static OperationState Copy(OperationState source) => new()
    {
        InitiatorId = source.InitiatorId,
        FlowCode = source.FlowCode,
        WithdrawBeforeFirstDecision = source.WithdrawBeforeFirstDecision,
        ReturnMode = source.ReturnMode,
        CancellationRequested = source.CancellationRequested,
        HasHumanDecision = source.HasHumanDecision,
        HumanDecisionCount = source.HumanDecisionCount,
        LastApprovalUndo = source.LastApprovalUndo is null ? null : new OperationUndoFrame
        {
            Snapshot = EngineStateCopy.Copy(source.LastApprovalUndo.Snapshot),
            ActorId = source.LastApprovalUndo.ActorId,
            TaskId = source.LastApprovalUndo.TaskId,
            DecisionCountAfter = source.LastApprovalUndo.DecisionCountAfter,
            ProcessedRequestCountAfter = source.LastApprovalUndo.ProcessedRequestCountAfter,
        },
        Pause = source.Pause is null ? null : new OperationPause
        {
            Kind = source.Pause.Kind,
            NodeId = source.Pause.NodeId,
            TokenId = source.Pause.TokenId,
            Mode = source.Pause.Mode,
            Forks = CopyForks(source.Pause.Forks),
        },
        LastReminders = new Dictionary<string, DateTimeOffset>(source.LastReminders, StringComparer.Ordinal),
        DraftOpinions = new Dictionary<string, string>(source.DraftOpinions, StringComparer.Ordinal),
        AssigneeOverrides = new Dictionary<string, string>(source.AssigneeOverrides, StringComparer.Ordinal),
        CopyRecipients = new HashSet<string>(source.CopyRecipients, StringComparer.Ordinal),
        Consultations = source.Consultations.Select(item => new OperationConsultation
        {
            Id = item.Id,
            TaskId = item.TaskId,
            RecipientId = item.RecipientId,
            Replied = item.Replied,
        }).ToList(),
        Visits = source.Visits.Select(item => new OperationVisit
        {
            NodeId = item.NodeId,
            Forks = CopyForks(item.Forks),
        }).ToList(),
    };

    /// <summary>
    /// 拷贝令牌的分叉路径。
    /// </summary>
    /// <param name="forks">原分叉路径。</param>
    /// <returns>独立的分叉路径。</returns>
    public static List<EngineForkFrame> CopyForks(IEnumerable<EngineForkFrame> forks) => forks.Select(item => new EngineForkFrame
    {
        ForkId = item.ForkId,
        PairId = item.PairId,
        BranchEdgeId = item.BranchEdgeId,
        ExpectedEdgeIds = [.. item.ExpectedEdgeIds],
    }).ToList();
}
