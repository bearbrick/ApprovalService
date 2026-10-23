using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 按角色查找人员，可限制在发起部门。
/// </summary>
internal sealed class RoleAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.Role;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        AssigneeLookup.Distinct(snapshot.People.Where(person =>
            person.Roles.Contains(request.Configuration.Value ?? string.Empty, StringComparer.Ordinal) &&
            (!request.LimitToInitiatingDepartment || person.DepartmentIds.Contains(snapshot.InitiatingDepartmentId, StringComparer.Ordinal)))
            .Select(person => person.Id));
}
