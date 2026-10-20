using ApprovalService.Domain.Files;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Files;

/// <summary>
/// 用独立数据库上下文保存附件元数据，并以条件更新协调关联和清理。
/// </summary>
/// <param name="factory">当前数据库提供程序的上下文工厂。</param>
public sealed class StoredFileRepository(IDbContextFactory<ApprovalDbContext> factory) : IStoredFileRepository
{
    /// <inheritdoc />
    public async Task AddAsync(StoredFileEntity file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        context.StoredFiles.Add(file);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StoredFileEntity?> FindAsync(string fileId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.StoredFiles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == fileId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> LinkAsync(string fileId, string instanceId, string uploaderId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var updated = await context.StoredFiles.Where(item => item.Id == fileId &&
                item.UploadedById == uploaderId && item.InstanceId == null && item.CleanupOwner == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.InstanceId, instanceId)
                .SetProperty(item => item.LinkedAt, now), cancellationToken);
        if (updated == 1)
        {
            return true;
        }

        // 同一文件重复引用同一审批单保持幂等，不允许改绑到其他审批单。
        return await context.StoredFiles.AsNoTracking().AnyAsync(item => item.Id == fileId &&
            item.UploadedById == uploaderId && item.InstanceId == instanceId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CanViewInstanceAsync(string instanceId, string userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var isInitiator = await context.Instances.AsNoTracking().AnyAsync(item => item.Id == instanceId &&
            (item.InitiatorId == userId || item.SubmittedById == userId), cancellationToken);
        if (isInitiator)
        {
            return true;
        }

        return await context.ApprovalTasks.AsNoTracking().AnyAsync(item => item.InstanceId == instanceId &&
            item.AssigneeId == userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredFileEntity>> FindUnusedAsync(DateTimeOffset cutoff, DateTimeOffset now,
        int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.StoredFiles.AsNoTracking()
            .Where(item => item.InstanceId == null && item.UploadedAt <= cutoff &&
                (item.CleanupOwner == null || item.CleanupLeaseUntil <= now))
            .OrderBy(item => item.UploadedAt).ThenBy(item => item.Id)
            .Take(limit).ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ClaimCleanupAsync(string fileId, DateTimeOffset cutoff, DateTimeOffset now,
        string owner, DateTimeOffset leaseUntil, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var updated = await context.StoredFiles.Where(item => item.Id == fileId && item.InstanceId == null &&
                item.UploadedAt <= cutoff && (item.CleanupOwner == null || item.CleanupLeaseUntil <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CleanupOwner, owner)
                .SetProperty(item => item.CleanupLeaseUntil, leaseUntil), cancellationToken);
        return updated == 1;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteClaimedAsync(string fileId, string owner,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var deleted = await context.StoredFiles.Where(item => item.Id == fileId && item.InstanceId == null &&
            item.CleanupOwner == owner).ExecuteDeleteAsync(cancellationToken);
        return deleted == 1;
    }
}
