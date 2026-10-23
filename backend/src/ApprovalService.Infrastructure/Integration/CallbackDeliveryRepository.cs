using ApprovalService.Domain.Integration;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 在数据库中原地重排回调，保持事件身份与正文不变。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class CallbackDeliveryRepository(IDbContextFactory<ApprovalDbContext> factory) : ICallbackDeliveryRepository
{
    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.OutboxItems.AsNoTracking().AnyAsync(item => item.Kind == "callback" && item.EventId == eventId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RequeueAsync(string eventId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var updated = await db.OutboxItems.Where(item => item.Kind == "callback" && item.EventId == eventId &&
                (item.Status != JobStatuses.Leased || item.LeaseUntil <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, JobStatuses.Pending)
                .SetProperty(item => item.AvailableAt, now)
                .SetProperty(item => item.CompletedAt, (DateTimeOffset?)null)
                .SetProperty(item => item.LastError, (string?)null)
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        return updated == 1;
    }
}
