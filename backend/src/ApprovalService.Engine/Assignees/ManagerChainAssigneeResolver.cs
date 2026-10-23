using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 从直属上级逐级找人，包含终点级或终点职级。
/// </summary>
internal sealed class ManagerChainAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.ManagerChain;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot)
    {
        var ids = new List<string>();
        var limit = Math.Max(1, request.Configuration.Level ?? snapshot.People.Count);
        for (var level = 1; level <= limit; level++)
        {
            var manager = AssigneeLookup.ManagerAt(snapshot, snapshot.InitiatorId, level);
            if (manager is null)
            {
                break;
            }

            ids.Add(manager.Id);
            if (request.StopAtGrade is not null && manager.Grade == request.StopAtGrade)
            {
                break;
            }
        }

        return AssigneeLookup.Distinct(ids);
    }
}
