using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Policies;

/// <summary>
/// 会签：全部同意才通过，可选择拒绝后立即结束或等待全员。
/// </summary>
internal sealed class AllApprovalPolicy : IApprovalPolicy
{
    /// <inheritdoc />
    public string Mode => FlowApprovalModes.All;

    /// <inheritdoc />
    public ApprovalPolicyDecision Evaluate(FlowApprovalSettings settings, IReadOnlyList<ApprovalBallot> ballots)
    {
        var rejected = ballots.Any(item => item.Status == EngineTaskStatuses.Rejected);
        var pending = ballots.Any(item => item.Status == EngineTaskStatuses.Pending);
        if (rejected && (settings.RejectBehavior != FlowRejectBehaviors.WaitAll || !pending))
        {
            return new ApprovalPolicyDecision { Outcome = "rejected" };
        }

        return pending ? new ApprovalPolicyDecision() : new ApprovalPolicyDecision { Outcome = "approved" };
    }
}
