using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Policies;

/// <summary>
/// 多人审批策略登记处。
/// </summary>
public static class ApprovalPolicyRegistry
{
    /// <summary>
    /// 内置策略。
    /// </summary>
    private static readonly IReadOnlyList<IApprovalPolicy> BuiltIns =
    [new SequentialApprovalPolicy(), new AllApprovalPolicy(), new AnyApprovalPolicy(), new VoteApprovalPolicy()];

    /// <summary>
    /// 根据流程设置与当前任务状态计算节点结果。
    /// </summary>
    /// <param name="settings">流程多人设置。</param>
    /// <param name="ballots">该节点的任务快照。</param>
    /// <param name="extensions">额外纯策略。</param>
    /// <returns>节点结果。</returns>
    public static ApprovalPolicyDecision Evaluate(FlowApprovalSettings settings, IReadOnlyList<ApprovalBallot> ballots,
        IReadOnlyList<IApprovalPolicy>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ballots);
        var policies = BuiltIns.Concat(extensions ?? []).ToArray();
        if (policies.GroupBy(item => item.Mode, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "多人审批方式编码重复。");
        }

        var policy = policies.SingleOrDefault(item => item.Mode == settings.Mode)
            ?? throw new EngineException(ErrorCodes.ValidationFailed, "不支持的多人审批方式。");
        return policy.Evaluate(settings, ballots);
    }
}
