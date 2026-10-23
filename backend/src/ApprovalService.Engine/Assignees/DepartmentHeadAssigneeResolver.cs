using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 按发起部门向上第 N 级部门负责人找人。
/// </summary>
internal sealed class DepartmentHeadAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.DepartmentHead;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        AssigneeLookup.Distinct([AssigneeLookup.DepartmentAt(snapshot, snapshot.InitiatingDepartmentId,
            Math.Max(0, (request.Configuration.Level ?? 1) - 1))?.HeadId]);
}
