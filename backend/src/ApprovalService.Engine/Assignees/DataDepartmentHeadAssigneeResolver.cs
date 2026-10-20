using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 按审批数据中的部门字段找负责人。
/// </summary>
internal sealed class DataDepartmentHeadAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.DataDepartmentHead;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot)
    {
        if (request.Configuration.FieldKey is null ||
            !snapshot.DataValues.TryGetValue(request.Configuration.FieldKey, out var departmentId))
        {
            return [];
        }

        return AssigneeLookup.Distinct([AssigneeLookup.DepartmentAt(snapshot, departmentId, 0)?.HeadId]);
    }
}
