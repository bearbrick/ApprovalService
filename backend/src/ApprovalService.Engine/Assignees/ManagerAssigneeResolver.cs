using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 查找发起人的第 N 级直属上级。
/// </summary>
internal sealed class ManagerAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.Manager;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        AssigneeLookup.Distinct([AssigneeLookup.ManagerAt(snapshot, snapshot.InitiatorId,
            Math.Max(1, request.Configuration.Level ?? 1))?.Id]);
}
