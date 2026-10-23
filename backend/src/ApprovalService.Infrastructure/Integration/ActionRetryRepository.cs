using ApprovalService.Domain.Integration;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 在数据库中重排达到上限的原自动动作待发项。
/// </summary>
/// <param name="factory">独立数据库上下文工厂。</param>
public sealed class ActionRetryRepository(IDbContextFactory<ApprovalDbContext> factory) : IActionRetryRepository
{
    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var item = await db.OutboxItems.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Kind == "engineEvent" && candidate.EventId == eventId, cancellationToken);
        return item is not null && ActionOutboxPayload.IsPendingAction(item);
    }

    /// <inheritdoc />
    public async Task<bool> RequeueAsync(string eventId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var updated = await db.OutboxItems.Where(item => item.Kind == "engineEvent" && item.EventId == eventId &&
                item.Status == JobStatuses.NeedsAttention)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, JobStatuses.Pending)
                .SetProperty(item => item.AvailableAt, now)
                .SetProperty(item => item.Attempts, 9)
                .SetProperty(item => item.LastError, (string?)null)
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        return updated == 1;
    }
}
