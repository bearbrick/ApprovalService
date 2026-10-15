namespace ApprovalService.Engine.Execution;

/// <summary>
/// 深拷贝运行状态，保证纯计算不修改调用者持有的对象。
/// </summary>
internal static class EngineStateCopy
{
    /// <summary>
    /// 拷贝审批单、任务、令牌及分叉栈。
    /// </summary>
    /// <param name="source">原状态。</param>
    /// <returns>与原对象无共享可变集合的新状态。</returns>
    public static EngineState Copy(EngineState source) => new()
    {
        InstanceId = source.InstanceId,
        Status = source.Status,
        StepCount = source.StepCount,
        ProcessedRequestIds = new HashSet<string>(source.ProcessedRequestIds, StringComparer.Ordinal),
        Tasks = source.Tasks.Select(task => new EngineTask
        {
            Id = task.Id,
            TokenId = task.TokenId,
            NodeId = task.NodeId,
            AssigneeId = task.AssigneeId,
            Status = task.Status,
            CompletionKind = task.CompletionKind,
            CanReject = task.CanReject,
        }).ToList(),
        Tokens = source.Tokens.Select(token => new EngineToken
        {
            Id = token.Id,
            NodeId = token.NodeId,
            IncomingEdgeId = token.IncomingEdgeId,
            WaitingFor = token.WaitingFor,
            Forks = token.Forks.Select(frame => new EngineForkFrame
            {
                ForkId = frame.ForkId,
                PairId = frame.PairId,
                BranchEdgeId = frame.BranchEdgeId,
                ExpectedEdgeIds = [.. frame.ExpectedEdgeIds],
            }).ToList(),
        }).ToList(),
    };
}
