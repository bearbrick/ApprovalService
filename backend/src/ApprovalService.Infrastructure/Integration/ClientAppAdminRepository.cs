using System.Data;
using System.Text.Json;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 在数据库事务中维护应用管理员名单及脱敏审计。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class ClientAppAdminRepository(IDbContextFactory<ApprovalDbContext> factory) : IClientAppAdminRepository
{
    /// <inheritdoc />
    public async Task<string?> GetAdministratorIdsJsonAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ClientApps.AsNoTracking().Where(item => item.Code == code)
            .Select(item => item.AdministratorPersonIdsJson).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ClientAppAdminWriteResult> ReplaceAsync(string code, string expectedJson, string updatedJson,
        IReadOnlyList<string> personIds, string actorId, string reason, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var current = await db.ClientApps.AsNoTracking().Where(item => item.Code == code)
            .Select(item => item.AdministratorPersonIdsJson).SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return ClientAppAdminWriteResult.AppNotFound;
        }

        if (!string.Equals(current, expectedJson, StringComparison.Ordinal))
        {
            return ClientAppAdminWriteResult.Conflict;
        }

        // 人员验证与条件写入在同一事务内完成，停用人员不能被重新授予权限。
        var activeCount = await db.People.AsNoTracking().CountAsync(item =>
            personIds.Contains(item.Id) && item.IsActive, cancellationToken);
        if (activeCount != personIds.Count)
        {
            return ClientAppAdminWriteResult.InvalidPerson;
        }

        if (current == updatedJson)
        {
            return ClientAppAdminWriteResult.Updated;
        }

        var changed = await db.ClientApps.Where(item => item.Code == code &&
            item.AdministratorPersonIdsJson == expectedJson)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AdministratorPersonIdsJson, updatedJson),
                cancellationToken);
        if (changed != 1)
        {
            return ClientAppAdminWriteResult.Conflict;
        }

        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "clientApp",
            EntityId = code,
            ActorId = actorId,
            Source = "approvalService",
            Kind = "administratorsUpdated",
            BeforeJson = JsonSerializer.Serialize(new { personIds = JsonSerializer.Deserialize<string[]>(current) ?? [] }),
            AfterJson = JsonSerializer.Serialize(new { personIds, reason }),
            OccurredAt = occurredAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ClientAppAdminWriteResult.Updated;
    }

    /// <inheritdoc />
    public async Task<bool> IsActiveAdministratorAsync(string code, string personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var json = await db.ClientApps.AsNoTracking().Where(item => item.Code == code && item.IsEnabled)
            .Select(item => item.AdministratorPersonIdsJson).SingleOrDefaultAsync(cancellationToken);
        if (json is null || !await db.People.AsNoTracking().AnyAsync(item =>
            item.Id == personId && item.IsActive, cancellationToken))
        {
            return false;
        }

        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? []).Contains(personId, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // 损坏配置不能扩大权限。
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsAppEnabledAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ClientApps.AsNoTracking().AnyAsync(item => item.Code == code && item.IsEnabled,
            cancellationToken);
    }
}
