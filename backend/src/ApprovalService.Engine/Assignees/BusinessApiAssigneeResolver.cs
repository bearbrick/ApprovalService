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
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot)
    {
        // 新取人结果按实际节点隔离；旧的纯引擎调用仍可按业务来源编码预取。
        if (snapshot.BusinessApiResults.TryGetValue(request.NodeId, out var byNode))
        {
            return AssigneeLookup.Distinct(byNode);
        }

        return request.Configuration.Value is not null &&
            snapshot.BusinessApiResults.TryGetValue(request.Configuration.Value, out var bySource)
                ? AssigneeLookup.Distinct(bySource) : [];
    }
}
