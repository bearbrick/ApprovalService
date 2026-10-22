using ApprovalService.Domain.Integration;
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
    public async Task UpdateSecretsAsync(ClientAppEntity app, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.ClientApps.SingleOrDefaultAsync(item => item.Code == app.Code, cancellationToken)
            ?? throw new InvalidOperationException("接入应用不存在，不能轮换密钥。");
        existing.ApiKeyHash = app.ApiKeyHash;
        existing.SigningSecretCiphertext = app.SigningSecretCiphertext;
        await db.SaveChangesAsync(cancellationToken);
    }
}
