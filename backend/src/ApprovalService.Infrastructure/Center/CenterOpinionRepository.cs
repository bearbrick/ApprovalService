using System.Data;
using ApprovalService.Domain.Center;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Center;

/// <summary>
/// 在数据库中按人员隔离保存常用意见。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class CenterOpinionRepository(IDbContextFactory<ApprovalDbContext> factory) : ICenterOpinionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CenterOpinionEntity>> ListAsync(string personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.CenterOpinions.AsNoTracking().Where(item => item.PersonId == personId)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(CenterOpinionEntity entity, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var count = await db.CenterOpinions.CountAsync(item => item.PersonId == entity.PersonId, cancellationToken);
        if (count >= 30)
        {
            return false;
        }
        db.CenterOpinions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<CenterOpinionEntity?> UpdateAsync(string personId, string id, string content,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.CenterOpinions.SingleOrDefaultAsync(item => item.PersonId == personId && item.Id == id,
            cancellationToken);
        if (entity is null)
        {
            return null;
        }
        entity.Content = content;
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string personId, string id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.CenterOpinions.SingleOrDefaultAsync(item => item.PersonId == personId && item.Id == id,
            cancellationToken);
        if (entity is null)
        {
            return false;
        }
        db.CenterOpinions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
