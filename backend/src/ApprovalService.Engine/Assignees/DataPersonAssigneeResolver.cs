using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 从审批数据人员字段中找人。
/// </summary>
internal sealed class DataPersonAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.DataPerson;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        request.Configuration.FieldKey is not null &&
        snapshot.DataValues.TryGetValue(request.Configuration.FieldKey, out var personId)
            ? AssigneeLookup.Distinct([personId]) : [];
}
