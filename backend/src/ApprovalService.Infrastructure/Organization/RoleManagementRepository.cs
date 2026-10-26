using System.Data;
using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Organization;

/// <summary>
/// 在串行化事务中重新核对当前角色并保护管理员可登录性。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class RoleManagementRepository(IDbContextFactory<ApprovalDbContext> factory)
    : IRoleManagementRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedRoleResponse>> ReadAsync(string actorId, string? code,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AuthorizeAsync(db, actorId, cancellationToken);
        var roles = await db.OrganizationRoles.AsNoTracking().Where(item => code == null || item.Code == code)
            .ToListAsync(cancellationToken);
        // 老库可能尚未分配流程管理员，内置角色以修订零出现，首次修改才建立持久行。
        foreach (var builtin in new[] { RoleManagementRules.OrganizationAdmin, RoleManagementRules.FlowAdmin })
        {
            if ((code is null || code == builtin) && roles.All(item => item.Code != builtin))
            {
                roles.Add(BuiltIn(builtin));
            }
        }
        if (code is not null && roles.Count == 0)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "角色不存在。");
        }
        var codes = roles.Select(item => item.Code).ToArray();
        var members = await db.PersonRoles.AsNoTracking().Where(item => codes.Contains(item.RoleCode))
            .ToListAsync(cancellationToken);
        var result = roles.OrderBy(item => item.Code, StringComparer.Ordinal)
            .Select(role => Response(role, members.Where(item => item.RoleCode == role.Code).Select(item => item.PersonId)))
            .ToArray();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<ManagedRoleResponse> SaveAsync(string actorId, string code, string? name,
        IReadOnlyList<string>? members, long? revision, string reason,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AuthorizeAsync(db, actorId, cancellationToken);
        var role = await db.OrganizationRoles.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        var creating = revision is null;
        if (creating && (role is not null || RoleManagementRules.IsBuiltIn(code)))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "角色编码已存在或属于系统内置角色。");
        }
        if (!creating && role is null && !RoleManagementRules.IsBuiltIn(code))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "角色不存在。");
        }
        var inserting = role is null;
        role ??= RoleManagementRules.IsBuiltIn(code) ? BuiltIn(code) : new OrganizationRoleEntity { Code = code };
        if (!creating && role.Revision != revision)
        {
            throw Conflict();
        }
        var previous = await db.PersonRoles.Where(item => item.RoleCode == code).ToListAsync(cancellationToken);
        var before = creating ? null : Response(role, previous.Select(item => item.PersonId));
        if (members is not null)
        {
            var added = members.Except(previous.Select(item => item.PersonId), StringComparer.Ordinal).ToArray();
            if (await db.People.CountAsync(item => added.Contains(item.Id) && item.IsActive, cancellationToken) != added.Length)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "新增角色成员必须是当前在职人员。");
            }
            if (code == RoleManagementRules.OrganizationAdmin && !await db.People.AnyAsync(person =>
                members.Contains(person.Id) && person.IsActive &&
                db.LocalAccounts.Any(account => account.PersonId == person.Id), cancellationToken))
            {
                throw new UserFriendlyException(ErrorCodes.Conflict, "必须保留至少一位已开通本地账号且在职的组织管理员。");
            }
            db.PersonRoles.RemoveRange(previous.Where(item => !members.Contains(item.PersonId)));
            db.PersonRoles.AddRange(added.Select(id => new PersonRoleEntity { PersonId = id, RoleCode = code }));
        }
        if (name is not null)
        {
            role.Name = name;
        }
        role.Revision++;
        if (inserting)
        {
            db.OrganizationRoles.Add(role);
        }
        var result = Response(role, members ?? previous.Select(item => item.PersonId));
        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "role",
            EntityId = code,
            ActorId = actorId,
            Source = "approvalService",
            Kind = creating ? "create" : members is null ? "rename" : "members",
            BeforeJson = before is null ? null : JsonSerializer.Serialize(new { role = before, reason }),
            AfterJson = JsonSerializer.Serialize(new { role = result, reason }),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict();
        }
        catch (DbUpdateException) when (inserting)
        {
            // 并发创建同编码可能在唯一键处失败；先释放失败事务，再确认是否已有成功提交。
            await transaction.RollbackAsync(cancellationToken);
            await using var current = await factory.CreateDbContextAsync(cancellationToken);
            if (await current.OrganizationRoles.AnyAsync(item => item.Code == code, cancellationToken))
            {
                throw Conflict();
            }
            throw;
        }
        return result;
    }

    /// <summary>
    /// 事务内再次核对人员在职、账号和持久管理员角色。
    /// </summary>
    /// <param name="db">当前上下文。</param>
    /// <param name="actorId">本地会话人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>权限检查完成。</returns>
    private static async Task AuthorizeAsync(ApprovalDbContext db, string actorId, CancellationToken cancellationToken)
    {
        if (!await db.People.AnyAsync(person => person.Id == actorId && person.IsActive &&
            db.LocalAccounts.Any(account => account.PersonId == actorId) &&
            db.PersonRoles.Any(role => role.PersonId == actorId && role.RoleCode == RoleManagementRules.OrganizationAdmin),
            cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "需要当前在职组织管理员的本地账号权限。");
        }
    }

    /// <summary>
    /// 构造尚未落库的内置角色。
    /// </summary>
    /// <param name="code">内置编码。</param>
    /// <returns>修订零的角色。</returns>
    private static OrganizationRoleEntity BuiltIn(string code) => new()
    {
        Code = code,
        Name = code == RoleManagementRules.OrganizationAdmin ? "组织管理员" : "流程管理员",
    };

    /// <summary>
    /// 构造稳定排序的角色摘要。
    /// </summary>
    /// <param name="role">角色状态。</param>
    /// <param name="members">成员 ID。</param>
    /// <returns>响应摘要。</returns>
    private static ManagedRoleResponse Response(OrganizationRoleEntity role, IEnumerable<string> members) => new()
    {
        Code = role.Code,
        Name = role.Name,
        Revision = role.Revision,
        IsBuiltIn = RoleManagementRules.IsBuiltIn(role.Code),
        PersonIds = members.Order(StringComparer.Ordinal).ToArray(),
    };

    /// <summary>
    /// 构造可刷新重试的冲突。
    /// </summary>
    /// <returns>统一冲突异常。</returns>
    private static UserFriendlyException Conflict() => new(ErrorCodes.Conflict, "角色已被其他管理员修改，请刷新后重试。");
}
