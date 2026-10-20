using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 从调用方预取的业务系统接口结果中找人，不发 HTTP 请求。
/// </summary>
internal sealed class BusinessApiAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.BusinessApi;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        request.Configuration.Value is not null &&
        snapshot.BusinessApiResults.TryGetValue(request.Configuration.Value, out var ids)
            ? AssigneeLookup.Distinct(ids) : [];
}
