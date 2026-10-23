using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 按最低职级查找人员。
/// </summary>
internal sealed class GradeAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.Grade;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot)
    {
        var minimum = snapshot.People.Where(person => person.Grade == request.Configuration.Value)
            .Select(person => person.GradeRank).DefaultIfEmpty(int.MaxValue).Min();
        return AssigneeLookup.Distinct(snapshot.People.Where(person => person.GradeRank >= minimum)
            .Select(person => person.Id));
    }
}
