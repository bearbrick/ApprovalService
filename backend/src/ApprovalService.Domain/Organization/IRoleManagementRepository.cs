using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Domain.Organization;

/// <summary>
/// 在一致事务内管理角色、成员与原因审计。
/// </summary>
public interface IRoleManagementRepository
{
    /// <summary>
    /// 按当前权限读取角色；编码为空时读取列表。
    /// </summary>
    /// <param name="actorId">当前本地人员。</param>
    /// <param name="code">可选角色编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>角色摘要列表。</returns>
    Task<IReadOnlyList<ManagedRoleResponse>> ReadAsync(string actorId, string? code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建或按修订更新名称、成员，并原子追加审计。
    /// </summary>
    /// <param name="actorId">当前本地人员。</param>
    /// <param name="code">不可变角色编码。</param>
    /// <param name="name">新名称，成员更新时为空。</param>
    /// <param name="members">目标成员，名称更新时为空。</param>
    /// <param name="revision">预期修订，创建时为空。</param>
    /// <param name="reason">审计原因。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>提交后的角色摘要。</returns>
    Task<ManagedRoleResponse> SaveAsync(string actorId, string code, string? name,
        IReadOnlyList<string>? members, long? revision, string reason,
        CancellationToken cancellationToken = default);
}
