using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 解析流程中指定的成员。
/// </summary>
internal sealed class MemberAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.Member;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        AssigneeLookup.Distinct((request.Configuration.Value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries));
}
