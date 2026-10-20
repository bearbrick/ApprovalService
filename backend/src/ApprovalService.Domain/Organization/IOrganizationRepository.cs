using ApprovalService.Engine.Assignees;

namespace ApprovalService.Domain.Organization;

/// <summary>
/// 组织主数据、外部绑定和变更日志的持久化边界。
/// </summary>
public interface IOrganizationRepository
{
    /// <summary>
    /// 读取用于导入检查的现有主数据。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>现有部门和人员。</returns>
    Task<OrganizationImportBatch> LoadMasterDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 将一批可信主数据和变更日志在同一事务内写入。
    /// </summary>
    /// <param name="batch">通过校验的批次。</param>
    /// <param name="actorId">操作人员 ID。</param>
    /// <param name="occurredAt">发生时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task SaveTrustedImportAsync(OrganizationImportBatch batch, string actorId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按接入应用来源推送变更；仅允许覆盖该来源所拥有的主数据。
    /// </summary>
    /// <param name="batch">通过校验的变更。</param>
    /// <param name="clientAppCode">接入应用编码。</param>
    /// <param name="actorId">操作人员 ID。</param>
    /// <param name="occurredAt">发生时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>需要交接提醒的离职人员 ID。</returns>
    Task<IReadOnlyList<string>> SavePushedChangesAsync(OrganizationImportBatch batch, string clientAppCode,
        string actorId, DateTimeOffset occurredAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工号或手机号查找唯一人员。
    /// </summary>
    /// <param name="employeeNumber">可空工号。</param>
    /// <param name="phone">可空手机号。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>唯一匹配人员；未匹配或多条匹配时为空。</returns>
    Task<PersonEntity?> MatchPersonAsync(string? employeeNumber, string? phone,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按内部人员 ID 读取人员。
    /// </summary>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>人员；不存在时为空。</returns>
    Task<PersonEntity?> GetPersonAsync(string personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入或替换外部人员绑定。
    /// </summary>
    /// <param name="binding">绑定关系。</param>
    /// <param name="actorId">操作者 ID。</param>
    /// <param name="occurredAt">发生时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task BindPersonAsync(ExternalPersonBindingEntity binding, string actorId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入或替换外部部门绑定。
    /// </summary>
    /// <param name="binding">绑定关系。</param>
    /// <param name="actorId">操作者 ID。</param>
    /// <param name="occurredAt">发生时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task BindDepartmentAsync(ExternalDepartmentBindingEntity binding, string actorId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把业务系统人员 ID 转成审批服务人员 ID。
    /// </summary>
    /// <param name="clientAppCode">接入应用编码。</param>
    /// <param name="externalUserId">业务系统人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>人员 ID；不存在时为空。</returns>
    Task<string?> ResolvePersonAsync(string clientAppCode, string externalUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把业务系统部门 ID 转成审批服务部门 ID。
    /// </summary>
    /// <param name="clientAppCode">接入应用编码。</param>
    /// <param name="externalDepartmentId">业务系统部门 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>部门 ID；不存在时为空。</returns>
    Task<string?> ResolveDepartmentAsync(string clientAppCode, string externalDepartmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取引擎找人的组织快照。
    /// </summary>
    /// <param name="initiatorId">发起人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>只含组织主数据的找人快照。</returns>
    Task<AssigneeSnapshot> LoadAssigneeSnapshotAsync(string initiatorId,
        CancellationToken cancellationToken = default);
}
