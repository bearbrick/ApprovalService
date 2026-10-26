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
            AllowedDocumentOriginsJson = item.AllowedDocumentOriginsJson,
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
    public async Task<bool> UpdateDocumentSettingsAsync(string code, string? expectedUrl, string expectedOriginsJson,
        string? updatedUrl, string updatedOriginsJson, string actorId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var changed = await db.ClientApps.Where(item => item.Code == code && item.DocumentUrl == expectedUrl &&
            item.AllowedDocumentOriginsJson == expectedOriginsJson)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DocumentUrl, updatedUrl)
                .SetProperty(item => item.AllowedDocumentOriginsJson, updatedOriginsJson), cancellationToken);
        if (changed != 1)
        {
            return false;
        }

        // 原单地址可能包含部署细节；审计只记录变更类别和原因。
        RecordAdminChange(db, code, actorId, "documentSettingsUpdated", occurredAt, null,
            JsonSerializer.Serialize(new
            {
                reason
            }));
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
    public async Task CreateWithAuditAsync(ClientAppEntity app, string actorId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        db.ClientApps.Add(app);
        RecordAdminChange(db, app.Code, actorId, "created", occurredAt, null,
            JsonSerializer.Serialize(new
            {
                app.Name,
                reason
            }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
    public async Task<bool> UpdateSecretsWithAuditAsync(ClientAppEntity app, string actorId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var changed = await db.ClientApps.Where(item => item.Code == app.Code &&
            item.AuthGeneration < long.MaxValue).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.ApiKeyHash, app.ApiKeyHash)
            .SetProperty(item => item.SigningSecretCiphertext, app.SigningSecretCiphertext)
            .SetProperty(item => item.AuthGeneration, item => item.AuthGeneration + 1), cancellationToken);
        if (changed != 1)
        {
            return false;
        }

        // 操作日志只记录轮换发生，不保存摘要、密文或本次生成的明文。
        RecordAdminChange(db, app.Code, actorId, "secretsRotated", occurredAt, null,
            JsonSerializer.Serialize(new
            {
                reason
            }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
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

    /// <inheritdoc />
    public async Task<bool> SetEnabledWithAuditAsync(string code, bool enabled, string actorId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var current = await db.ClientApps.AsNoTracking().SingleOrDefaultAsync(item => item.Code == code,
            cancellationToken);
        if (current is null || current.IsEnabled == enabled)
        {
            return false;
        }

        var changed = await db.ClientApps.Where(item => item.Code == code &&
            item.IsEnabled == current.IsEnabled && item.AuthGeneration == current.AuthGeneration &&
            item.AuthGeneration < long.MaxValue).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsEnabled, enabled)
                .SetProperty(item => item.AuthGeneration, item => item.AuthGeneration + 1), cancellationToken);
        if (changed != 1)
        {
            return false;
        }

        RecordAdminChange(db, code, actorId, "enabledChanged", occurredAt,
            JsonSerializer.Serialize(new
            {
                enabled = current.IsEnabled
            }),
            JsonSerializer.Serialize(new
            {
                enabled,
                reason
            }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// 将脱敏应用变更摘要加入当前事务；严禁传入密钥或出站地址。
    /// </summary>
    /// <param name="db">当前数据库事务上下文。</param>
    /// <param name="code">接入应用编码。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="kind">变更类别。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="beforeJson">脱敏前值。</param>
    /// <param name="afterJson">脱敏后值。</param>
    private static void RecordAdminChange(ApprovalDbContext db, string code, string actorId, string kind,
        DateTimeOffset occurredAt, string? beforeJson, string afterJson)
    {
        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "clientApp",
            EntityId = code,
            ActorId = actorId,
            Source = "approvalService",
            Kind = kind,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            OccurredAt = occurredAt,
        });
    }
}
