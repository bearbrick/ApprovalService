using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Policies;

/// <summary>
/// 或签：任一人同意即通过，全部反对才拒绝。
/// </summary>
internal sealed class AnyApprovalPolicy : IApprovalPolicy
{
    /// <inheritdoc />
    public string Mode => FlowApprovalModes.Any;

    /// <inheritdoc />
    public ApprovalPolicyDecision Evaluate(FlowApprovalSettings settings, IReadOnlyList<ApprovalBallot> ballots)
    {
        if (ballots.Any(item => item.Status == EngineTaskStatuses.Approved))
        {
            return new ApprovalPolicyDecision { Outcome = "approved" };
        }

        return ballots.Any(item => item.Status == EngineTaskStatuses.Pending)
            ? new ApprovalPolicyDecision() : new ApprovalPolicyDecision { Outcome = "rejected" };
    }
}
