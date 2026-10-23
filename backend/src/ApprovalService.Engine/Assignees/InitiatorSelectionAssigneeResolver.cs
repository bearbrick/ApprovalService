using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 读取业务系统发起时选择的本节点人员。
/// </summary>
internal sealed class InitiatorSelectionAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.InitiatorSelection;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        snapshot.InitiatorSelections.TryGetValue(request.NodeId, out var ids)
            ? AssigneeLookup.Distinct(ids) : [];
}
