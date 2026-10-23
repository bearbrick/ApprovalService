using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 读取上一步审批人对本节点指定的人员。
/// </summary>
internal sealed class PreviousSelectionAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.PreviousSelection;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        snapshot.PreviousSelections.TryGetValue(request.NodeId, out var ids)
            ? AssigneeLookup.Distinct(ids) : [];
}
