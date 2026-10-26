using ApprovalService.Domain.Center;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Center;

/// <summary>
/// 以不可变行保存已校验 PNG，读取不跟踪实体。
/// </summary>
/// <param name="factory">按当前数据库提供程序建立上下文。</param>
public sealed class SignatureAssetRepository(IDbContextFactory<ApprovalDbContext> factory) : ISignatureAssetRepository
{
    /// <inheritdoc />
    public async Task AddAsync(SignatureAssetEntity asset, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.SignatureAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SignatureAssetEntity?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.SignatureAssets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SignatureAssetEntity?> FindLatestPersonalAsync(string personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.SignatureAssets.AsNoTracking().Where(item => item.PersonId == personId && item.IsPersonal)
            .OrderByDescending(item => item.UploadedAt).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
