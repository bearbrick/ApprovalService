using ApprovalService.Engine.Assignees;

namespace ApprovalService.Application.Organization;

/// <summary>
/// 开放 API 使用的可信组织解析入口；不接受调用方自造的找人快照。
/// </summary>
public interface IOrganizationLookupAppService
{
    /// <summary>
    /// 将接入应用人员 ID 或工号解析为在职的内部人员 ID。
    /// </summary>
    /// <param name="clientAppCode">已鉴权的接入应用编码。</param>
    /// <param name="userId">业务系统人员 ID 或工号。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>内部人员 ID；不存在或已离职时为空。</returns>
    Task<string?> ResolvePersonAsync(string clientAppCode, string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将接入应用部门 ID 或内部部门编码解析为内部部门 ID。
    /// </summary>
    /// <param name="clientAppCode">已鉴权的接入应用编码。</param>
    /// <param name="departmentId">业务系统部门 ID 或内部部门编码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>内部部门 ID；不存在时为空。</returns>
    Task<string?> ResolveDepartmentAsync(string clientAppCode, string departmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为节点到达构造可信的组织找人快照。
    /// </summary>
    /// <param name="initiatorId">已解析的内部发起人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>组织主数据快照；业务数据和经过授权的外部取人结果由调用方补充。</returns>
    Task<AssigneeSnapshot> LoadAssigneeSnapshotAsync(string initiatorId,
        CancellationToken cancellationToken = default);
}
