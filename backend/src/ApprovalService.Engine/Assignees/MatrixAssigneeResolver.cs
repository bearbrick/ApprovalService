using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 从调用方已匹配的审批矩阵快照中找人。
/// </summary>
internal sealed class MatrixAssigneeResolver : IAssigneeResolver
{
    /// <inheritdoc />
    public string Source => FlowAssigneeSources.Matrix;

    /// <inheritdoc />
    public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot) =>
        AssigneeLookup.Distinct(snapshot.MatrixMatches.Where(item => item.MatrixCode == request.Configuration.Value)
            .SelectMany(item => item.AssigneeIds));
}
