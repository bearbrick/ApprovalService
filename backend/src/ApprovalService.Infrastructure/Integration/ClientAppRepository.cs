using System.Data;
using System.Text.Json;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 接入应用的 EF Core 仓储；API Key 只按摘要查询。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class ClientAppRepository(IDbContextFactory<ApprovalDbContext> factory) : IClientAppRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientAppEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ClientApps.AsNoTracking().Select(item => new ClientAppEntity
        {
            Code = item.Code,
            Name = item.Name,
            AllowedFlowCodesJson = item.AllowedFlowCodesJson,
            IsEnabled = item.IsEnabled,
        }).ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAllowedEmbedOriginsAsync(string code, string expectedJson, string updatedJson,
        string actorId, string reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        if (expectedJson == updatedJson)
        {
            return await FindByCodeAsync(code, cancellationToken) is { } app &&
                app.AllowedEmbedOriginsJson == expectedJson;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var changed = await db.ClientApps.Where(item => item.Code == code &&
            item.AllowedEmbedOriginsJson == expectedJson)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AllowedEmbedOriginsJson, updatedJson),
                cancellationToken);
        if (changed != 1)
        {
            return false;
        }

        // 只记录允许来源及原因，绝不把应用实体中的密钥字段写进审计快照。
        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "clientApp",
            EntityId = code,
            ActorId = actorId,
            Source = "approvalService",
            Kind = "embedOriginsUpdated",
            BeforeJson = JsonSerializer.Serialize(new
            {
                origins = JsonSerializer.Deserialize<string[]>(expectedJson) ?? [],
            }),
            AfterJson = JsonSerializer.Serialize(new
            {
                origins = JsonSerializer.Deserialize<string[]>(updatedJson) ?? [],
                reason,
            }),
            OccurredAt = occurredAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<ClientAppEntity?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ClientApps.AsNoTracking().SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ClientAppEntity?> FindActiveByKeyHashAsync(string apiKeyHash,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ClientApps.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ApiKeyHash == apiKeyHash && item.IsEnabled, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CreateAsync(ClientAppEntity app, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.ClientApps.Add(app);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateSecretsAsync(ClientAppEntity app, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        // 密钥与代际须在同一条 SQL 中更新，避免并发启停或轮换丢失版本。
        var changed = await db.ClientApps.Where(item => item.Code == app.Code &&
            item.AuthGeneration < long.MaxValue).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.ApiKeyHash, app.ApiKeyHash)
            .SetProperty(item => item.SigningSecretCiphertext, app.SigningSecretCiphertext)
            .SetProperty(item => item.AuthGeneration, item => item.AuthGeneration + 1), cancellationToken);
        return changed == 1;
    }

    /// <inheritdoc />
    public async Task<bool> SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var changed = await db.ClientApps.Where(item => item.Code == code &&
            item.IsEnabled != enabled && item.AuthGeneration < long.MaxValue)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsEnabled, enabled)
                .SetProperty(item => item.AuthGeneration, item => item.AuthGeneration + 1), cancellationToken);
        return changed == 1;
    }
}
