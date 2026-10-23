using System.Text.Json;
using ApprovalService.Domain.Administration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Administration;

/// <summary>
/// 管理列表专用的只读 EF 查询，仅投影可展示的字段。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class AdminConsoleRepository(IDbContextFactory<ApprovalDbContext> factory) : IAdminConsoleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminBindingRecord>> ListBindingsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var people = await db.ExternalPersonBindings.AsNoTracking().Select(item => new AdminBindingRecord
        {
            ClientAppCode = item.ClientAppCode,
            Kind = "person",
            ExternalId = item.ExternalUserId,
            InternalId = item.PersonId,
            Method = item.Method,
        }).ToArrayAsync(cancellationToken);
        var departments = await db.ExternalDepartmentBindings.AsNoTracking().Select(item => new AdminBindingRecord
        {
            ClientAppCode = item.ClientAppCode,
            Kind = "department",
            ExternalId = item.ExternalDepartmentId,
            InternalId = item.DepartmentId,
            Method = item.Method,
        }).ToArrayAsync(cancellationToken);
        return people.Concat(departments).OrderBy(item => item.ClientAppCode, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.ExternalId, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminAuditRecord>> ListAuditAsync(int limit,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var changes = await db.OrganizationChanges.AsNoTracking()
            .OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id)
            .Take(limit).Select(item => new
            {
                item.OccurredAt,
                item.ActorId,
                item.EntityKind,
                item.EntityId,
                item.Kind,
                item.AfterJson,
            }).ToArrayAsync(cancellationToken);
        return changes.Select(item => new AdminAuditRecord
        {
            OccurredAt = item.OccurredAt,
            ActorId = item.ActorId,
            EntityKind = item.EntityKind,
            EntityId = item.EntityId,
            Kind = item.Kind,
            Reason = item.EntityKind == "clientApp" ? ReadReason(item.AfterJson) : null,
        }).ToArray();
    }

    /// <summary>
    /// 仅从本服务生成的接入应用审计数据中提取原因，其他快照内容不传出仓储。
    /// </summary>
    /// <param name="json">审计数据快照。</param>
    /// <returns>显式填写的变更原因。</returns>
    private static string? ReadReason(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("reason", out var reason) &&
            reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
    }
}
