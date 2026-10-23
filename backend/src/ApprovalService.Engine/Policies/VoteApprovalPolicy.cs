using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Policies;

/// <summary>
/// 票签：按人员权重和通过比例计算，无法达标时提前拒绝。
/// </summary>
internal sealed class VoteApprovalPolicy : IApprovalPolicy
{
    /// <inheritdoc />
    public string Mode => FlowApprovalModes.Vote;

    /// <inheritdoc />
    public ApprovalPolicyDecision Evaluate(FlowApprovalSettings settings, IReadOnlyList<ApprovalBallot> ballots)
    {
        var threshold = settings.VoteThreshold;
        if (threshold is null or <= 0 or > 1 || ballots.Count == 0)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "票签比例必须大于零且不超过一，并且至少有一名审批人。");
        }

        decimal total = 0;
        decimal approved = 0;
        decimal pending = 0;
        foreach (var ballot in ballots)
        {
            var weight = settings.VoteWeights.GetValueOrDefault(ballot.AssigneeId, 1);
            if (weight <= 0)
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "票签人员权重必须大于零。");
            }

            total += weight;
            if (ballot.Status == EngineTaskStatuses.Approved)
            {
                approved += weight;
            }
            else if (ballot.Status == EngineTaskStatuses.Pending)
            {
                pending += weight;
            }
        }

        // 比较乘积可避免比例目标先四舍五入造成边界误判。
        if (approved >= total * threshold.Value)
        {
            return new ApprovalPolicyDecision { Outcome = "approved" };
        }

        return approved + pending < total * threshold.Value
            ? new ApprovalPolicyDecision { Outcome = "rejected" } : new ApprovalPolicyDecision();
    }
}
