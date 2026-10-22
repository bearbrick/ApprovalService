using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Policies;

/// <summary>
/// 依次审批：前一人同意后才激活下一人。
/// </summary>
internal sealed class SequentialApprovalPolicy : IApprovalPolicy
{
    /// <inheritdoc />
    public string Mode => FlowApprovalModes.Sequential;

    /// <inheritdoc />
    public ApprovalPolicyDecision Evaluate(FlowApprovalSettings settings, IReadOnlyList<ApprovalBallot> ballots)
    {
        if (ballots.Any(item => item.Status == EngineTaskStatuses.Rejected))
        {
            return new ApprovalPolicyDecision { Outcome = "rejected" };
        }

        if (ballots.Any(item => item.Status == EngineTaskStatuses.Pending))
        {
            return new ApprovalPolicyDecision();
        }

        var next = ballots.FirstOrDefault(item => item.Status == EngineTaskStatuses.Queued);
        return next is null
            ? new ApprovalPolicyDecision { Outcome = "approved" }
            : new ApprovalPolicyDecision { ActivateTaskId = next.TaskId };
    }
}
