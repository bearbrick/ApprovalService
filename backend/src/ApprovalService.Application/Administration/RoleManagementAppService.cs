using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 校验角色管理输入并协调当前授权与审计事务。
/// </summary>
/// <param name="repository">角色管理仓储。</param>
public sealed class RoleManagementAppService(IRoleManagementRepository repository)
{
    /// <summary>
    /// 读取当前角色目录。
    /// </summary>
    /// <param name="actorId">已验证的本地人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>完整角色目录。</returns>
    public Task<IReadOnlyList<ManagedRoleResponse>> ListAsync(string actorId, CancellationToken cancellationToken) =>
        repository.ReadAsync(actorId, null, cancellationToken);

    /// <summary>
    /// 读取单个角色与成员。
    /// </summary>
    /// <param name="code">角色编码。</param>
    /// <param name="actorId">已验证的本地人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>角色摘要。</returns>
    public async Task<ManagedRoleResponse> GetAsync(string code, string actorId, CancellationToken cancellationToken) =>
        (await repository.ReadAsync(actorId, RoleManagementRules.Code(code), cancellationToken)).Single();

    /// <summary>
    /// 创建无成员的自定义角色。
    /// </summary>
    /// <param name="request">编码、名称和原因。</param>
    /// <param name="actorId">已验证的本地人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>新角色。</returns>
    public Task<ManagedRoleResponse> CreateAsync(CreateManagedRoleRequest request, string actorId,
        CancellationToken cancellationToken) => repository.SaveAsync(actorId, RoleManagementRules.Code(request.Code),
        RoleManagementRules.Text(request.Name, false), null, null, RoleManagementRules.Text(request.Reason, true), cancellationToken);

    /// <summary>
    /// 按修订修改名称。
    /// </summary>
    /// <param name="code">不可变编码。</param>
    /// <param name="request">名称、修订和原因。</param>
    /// <param name="actorId">已验证的本地人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>更新后的角色。</returns>
    public Task<ManagedRoleResponse> RenameAsync(string code, UpdateManagedRoleRequest request, string actorId,
        CancellationToken cancellationToken) => repository.SaveAsync(actorId, RoleManagementRules.Code(code),
        RoleManagementRules.Text(request.Name, false), null, Revision(request.ExpectedRevision),
        RoleManagementRules.Text(request.Reason, true), cancellationToken);

    /// <summary>
    /// 按修订替换成员。
    /// </summary>
    /// <param name="code">不可变编码。</param>
    /// <param name="request">成员、修订和原因。</param>
    /// <param name="actorId">已验证的本地人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>更新后的角色。</returns>
    public Task<ManagedRoleResponse> MembersAsync(string code, UpdateRoleMembersRequest request, string actorId,
        CancellationToken cancellationToken) => repository.SaveAsync(actorId, RoleManagementRules.Code(code), null,
        RoleManagementRules.Members(request.PersonIds), Revision(request.ExpectedRevision),
        RoleManagementRules.Text(request.Reason, true), cancellationToken);

    /// <summary>
    /// 拒绝负数修订。
    /// </summary>
    /// <param name="revision">预期修订。</param>
    /// <returns>有效修订。</returns>
    private static long Revision(long revision) => revision >= 0 ? revision :
        throw new UserFriendlyException(ErrorCodes.ValidationFailed, "角色修订不能小于零。");
}
