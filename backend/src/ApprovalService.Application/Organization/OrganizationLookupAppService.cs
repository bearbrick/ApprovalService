using ApprovalService.Domain.Organization;
using ApprovalService.Engine.Assignees;

namespace ApprovalService.Application.Organization;

/// <summary>
/// 在应用边界统一解析业务系统标识和组织快照。
/// </summary>
/// <param name="repository">组织主数据与绑定仓储。</param>
public sealed class OrganizationLookupAppService(IOrganizationRepository repository) : IOrganizationLookupAppService
{
    /// <inheritdoc />
    public async Task<string?> ResolvePersonAsync(string clientAppCode, string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientAppCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var boundId = await repository.ResolvePersonAsync(clientAppCode, userId, cancellationToken);
        var person = boundId is null
            ? await repository.MatchPersonAsync(userId, null, cancellationToken)
            : await repository.GetPersonAsync(boundId, cancellationToken);
        return person is { IsActive: true } ? person.Id : null;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveBoundPersonAsync(string clientAppCode, string externalUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientAppCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalUserId);
        var personId = await repository.ResolvePersonAsync(clientAppCode, externalUserId, cancellationToken);
        var person = personId is null ? null : await repository.GetPersonAsync(personId, cancellationToken);
        return person is { IsActive: true } ? person.Id : null;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveDepartmentAsync(string clientAppCode, string departmentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientAppCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(departmentId);
        var boundId = await repository.ResolveDepartmentAsync(clientAppCode, departmentId, cancellationToken);
        if (boundId is not null)
        {
            return boundId;
        }

        var master = await repository.LoadMasterDataAsync(cancellationToken);
        return master.Departments.SingleOrDefault(item => item.Id == departmentId || item.Code == departmentId)?.Id;
    }

    /// <inheritdoc />
    public Task<AssigneeSnapshot> LoadAssigneeSnapshotAsync(string initiatorId,
        CancellationToken cancellationToken = default) =>
        repository.LoadAssigneeSnapshotAsync(initiatorId, cancellationToken);
}
