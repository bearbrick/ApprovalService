using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 按发起部门的分管关系找领导。
/// </summary>
internal sealed class DivisionLeaderAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.DivisionLeader;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot)
    {
        var department = AssigneeLookup.DepartmentAt(snapshot, snapshot.InitiatingDepartmentId, 0);
        if (department is null)
        {
            return [];
        }

        return request.Configuration.Value is not null
            ? department.DivisionLeadersByArea.TryGetValue(request.Configuration.Value, out var ids)
                ? AssigneeLookup.Distinct(ids) : []
            : AssigneeLookup.Distinct(department.DivisionLeaderIds);
    }
}
