using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 注册内置审批人来源，并允许调用方追加纯解析器。
/// </summary>
public static class AssigneeResolverRegistry
{
    /// <summary>
    /// 所有内置来源的解析器。
    /// </summary>
    private static readonly IReadOnlyList<IAssigneeResolver> BuiltIns =
    [
        new MemberAssigneeResolver(), new RoleAssigneeResolver(), new PositionAssigneeResolver(),
        new GradeAssigneeResolver(), new ManagerAssigneeResolver(), new ManagerChainAssigneeResolver(),
        new DepartmentHeadAssigneeResolver(), new DataDepartmentHeadAssigneeResolver(),
        new DivisionLeaderAssigneeResolver(), new DataPersonAssigneeResolver(), new MatrixAssigneeResolver(),
        new BusinessApiAssigneeResolver(), new PreviousSelectionAssigneeResolver(),
        new InitiatorSelectionAssigneeResolver(), new InitiatorAssigneeResolver(),
    ];

    /// <summary>
    /// 按流程来源在当前快照解析人员，确保扩展来源不重名。
    /// </summary>
    /// <param name="request">节点请求。</param>
    /// <param name="snapshot">到节点时的输入快照。</param>
    /// <param name="extensions">额外的纯来源解析器。</param>
    /// <returns>有序且唯一的人员 ID。</returns>
    public static AssigneeResolution Resolve(AssigneeRequest request, AssigneeSnapshot snapshot,
        IReadOnlyList<IAssigneeResolver>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var resolvers = BuiltIns.Concat(extensions ?? []).ToArray();
        if (resolvers.GroupBy(item => item.Source, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "审批人来源编码重复。");
        }

        var resolver = resolvers.SingleOrDefault(item => item.Source == request.Configuration.Source)
            ?? throw new EngineException(ErrorCodes.ValidationFailed, "不支持的审批人来源。");
        // 扩展解析器只接收隔离副本，不能改变引擎后续使用的组织快照。
        var requestCopy = JsonSerializer.Deserialize<AssigneeRequest>(
            JsonSerializer.Serialize(request, ApprovalJson.Options), ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批人请求复制失败。");
        var snapshotCopy = JsonSerializer.Deserialize<AssigneeSnapshot>(
            JsonSerializer.Serialize(snapshot, ApprovalJson.Options), ApprovalJson.Options)
            ?? throw new InvalidOperationException("组织快照复制失败。");
        return new AssigneeResolution { AssigneeIds = AssigneeLookup.Distinct(resolver.Resolve(requestCopy, snapshotCopy)).ToList() };
    }
}
