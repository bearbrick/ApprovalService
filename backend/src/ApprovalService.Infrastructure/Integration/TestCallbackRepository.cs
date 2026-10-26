using System.Data;
using System.Text.Json;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 在同一数据库事务中重验权限、固定测试回调正文及操作原因。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class TestCallbackRepository(IDbContextFactory<ApprovalDbContext> factory) : ITestCallbackRepository
{
    /// <inheritdoc />
    public async Task<TestCallbackEnqueueResult> EnqueueAsync(string code, string actorId, string eventId,
        string payloadJson, string reason, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var app = await db.ClientApps.AsNoTracking().Where(item => item.Code == code).Select(item => new
        {
            item.IsEnabled,
            item.CallbackUrl,
            item.AdministratorPersonIdsJson,
        }).SingleOrDefaultAsync(cancellationToken);
        if (app is null || !app.IsEnabled)
        {
            return TestCallbackEnqueueResult.AppUnavailable;
        }

        if (string.IsNullOrWhiteSpace(app.CallbackUrl))
        {
            return TestCallbackEnqueueResult.CallbackUrlMissing;
        }

        var active = await db.People.AsNoTracking().AnyAsync(item => item.Id == actorId && item.IsActive,
            cancellationToken);
        if (!active)
        {
            return TestCallbackEnqueueResult.Forbidden;
        }

        var organizationAdmin = await db.PersonRoles.AsNoTracking().AnyAsync(item =>
            item.PersonId == actorId && item.RoleCode == "organization-admin", cancellationToken);
        if (!organizationAdmin && !ContainsAdministrator(app.AdministratorPersonIdsJson, actorId))
        {
            return TestCallbackEnqueueResult.Forbidden;
        }

        // 用合成 ID 占用待发项现有非空来源列，但不创建审批单、任务或业务动作。
        db.OutboxItems.Add(new OutboxItemEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = eventId,
            EventId = eventId,
            Kind = "callback",
            PayloadJson = payloadJson,
            Status = JobStatuses.Pending,
            AvailableAt = occurredAt,
        });
        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "callback",
            EntityId = eventId,
            ActorId = actorId,
            Source = "approvalService",
            Kind = "testQueued",
            AfterJson = JsonSerializer.Serialize(new { clientAppCode = code, reason }),
            OccurredAt = occurredAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TestCallbackEnqueueResult.Queued;
    }

    /// <summary>
    /// 损坏的持久名单不授予任何人员权限。
    /// </summary>
    /// <param name="json">应用管理员名单 JSON。</param>
    /// <param name="actorId">当前人员 ID。</param>
    /// <returns>名单明确包含此人员时为真。</returns>
    private static bool ContainsAdministrator(string json, string actorId)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? []).Contains(actorId, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
