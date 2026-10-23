using System.Data;
using System.Text.Json;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
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
    public async Task<bool> RequeueWithAuditAsync(string eventId, DateTimeOffset now, string actorId,
        string reason, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
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
        if (updated != 1)
        {
            return false;
        }

        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "callback",
            EntityId = eventId,
            ActorId = actorId,
            Source = "approvalService",
            Kind = "retried",
            AfterJson = JsonSerializer.Serialize(new { reason }),
            OccurredAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

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
