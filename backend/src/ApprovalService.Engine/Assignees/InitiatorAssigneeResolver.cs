using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 将发起人本人作为确认节点处理人。
/// </summary>
internal sealed class InitiatorAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.Initiator;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        AssigneeLookup.Distinct([snapshot.InitiatorId]);
}
