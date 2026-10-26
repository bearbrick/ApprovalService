using System.Text.Json;
using ApprovalService.Domain.Administration;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Administration;

/// <summary>
/// 工作日历及管理审计的原子持久化。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class WorkCalendarRepository(IDbContextFactory<ApprovalDbContext> factory) : IWorkCalendarRepository
{
    /// <inheritdoc />
    public async Task<WorkCalendarEntity?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Set<WorkCalendarEntity>().AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == "global", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(WorkCalendarEntity value, long expectedRevision, string actorId,
        string reason, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var current = await db.Set<WorkCalendarEntity>().SingleOrDefaultAsync(item => item.Id == "global",
            cancellationToken);
        if ((current?.Revision ?? 0) != expectedRevision)
        {
            return false;
        }
        var before = current is null ? null : JsonSerializer.Serialize(current);
        if (current is null)
        {
            db.Add(value);
        }
        else
        {
            current.Revision = expectedRevision + 1;
            current.WorkingStart = value.WorkingStart;
            current.WorkingEnd = value.WorkingEnd;
            current.OverridesJson = value.OverridesJson;
        }
        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "workCalendar",
            EntityId = "global",
            ActorId = actorId,
            Source = "approvalService",
            Kind = "update",
            BeforeJson = before,
            AfterJson = JsonSerializer.Serialize(new
            {
                value.Revision,
                value.WorkingStart,
                value.WorkingEnd,
                value.OverridesJson,
                reason
            }),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        try
        {
            // EF 的一次保存事务包含带旧修订的条件更新和审计；抢输者整体回滚。
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException) when (current is null &&
            db.Set<WorkCalendarEntity>().AsNoTracking().Any(item => item.Id == "global"))
        {
            // 空库同时保存由唯一键裁决，与旧修订冲突返回相同语义。
            return false;
        }
    }
}
