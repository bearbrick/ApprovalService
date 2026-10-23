using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// 用独立数据库上下文保存待发通知和站内收件箱。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class NotificationRepository(IDbContextFactory<ApprovalDbContext> factory) : INotificationRepository
{
    /// <inheritdoc />
    public async Task EnqueueAsync(OutboxItemEntity item, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.OutboxItems.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == item.Id,
            cancellationToken);
        if (existing is not null)
        {
            EnsureSame(existing, item);
            return;
        }

        db.OutboxItems.Add(item);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 两个调用者同时插入同一 ID 时只接受完全一致的内容；其他数据库错误原样抛出。
            await using var verification = await factory.CreateDbContextAsync(cancellationToken);
            existing = await verification.OutboxItems.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == item.Id, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            EnsureSame(existing, item);
        }
    }

    /// <inheritdoc />
    public async Task SaveInAppAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.InAppNotifications.AsNoTracking().SingleOrDefaultAsync(item => item.Id == message.Id,
            cancellationToken);
        if (existing is not null)
        {
            EnsureSame(existing, message);
            return;
        }

        db.InAppNotifications.Add(new InAppNotificationEntity
        {
            Id = message.Id,
            RecipientId = message.RecipientId,
            Subject = message.Subject,
            Body = message.Body,
            TargetPath = message.TargetPath,
            CreatedAt = message.CreatedAt,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await using var verification = await factory.CreateDbContextAsync(cancellationToken);
            existing = await verification.InAppNotifications.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == message.Id, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            EnsureSame(existing, message);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InAppNotificationEntity>> ListAsync(string recipientId, int take,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.InAppNotifications.AsNoTracking().Where(item => item.RecipientId == recipientId)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Take(take).ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> MarkReadAsync(string id, string recipientId, DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var changed = await db.InAppNotifications.Where(item => item.Id == id &&
                item.RecipientId == recipientId && item.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ReadAt, readAt), cancellationToken);
        return changed > 0 || await db.InAppNotifications.AsNoTracking().AnyAsync(item =>
            item.Id == id && item.RecipientId == recipientId, cancellationToken);
    }

    /// <summary>
    /// 同一个消息 ID 只能代表同一种通知内容；队列状态变化不影响幂等检查。
    /// </summary>
    /// <param name="existing">已存待发项。</param>
    /// <param name="proposed">本次请求。</param>
    private static void EnsureSame(OutboxItemEntity existing, OutboxItemEntity proposed)
    {
        if (existing.Kind != proposed.Kind || existing.EventId != proposed.EventId ||
            existing.InstanceId != proposed.InstanceId || existing.PayloadJson != proposed.PayloadJson)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "通知 ID 已用于其他内容。");
        }
    }

    /// <summary>
    /// 重试只接受完全相同的收件箱内容，避免 ID 冲突覆盖原消息。
    /// </summary>
    /// <param name="existing">已有站内消息。</param>
    /// <param name="message">本次投递。</param>
    private static void EnsureSame(InAppNotificationEntity existing, NotificationMessage message)
    {
        if (existing.RecipientId != message.RecipientId || existing.Subject != message.Subject ||
            existing.Body != message.Body || existing.TargetPath != message.TargetPath ||
            existing.CreatedAt != message.CreatedAt)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "通知 ID 已用于其他站内消息。");
        }
    }
}
