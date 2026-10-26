using System.Text.Json;
using ApprovalService.Domain.Administration;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Administration;

/// <summary>
/// 用同一数据库事务保存全局品牌设置与脱敏管理审计。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class SystemBrandingRepository(IDbContextFactory<ApprovalDbContext> factory)
    : ISystemBrandingRepository
{
    /// <inheritdoc />
    public async Task<SystemBrandingEntity?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.SystemBranding.AsNoTracking().SingleOrDefaultAsync(item => item.Id == "global",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(SystemBrandingEntity value, long expectedRevision, string actorId,
        string reason, string action, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var current = await db.SystemBranding.SingleOrDefaultAsync(item => item.Id == "global",
            cancellationToken);
        if ((current?.Revision ?? 0) != expectedRevision)
        {
            return false;
        }
        var before = current is null ? null : AuditSnapshot(current, reason);
        if (current is null)
        {
            db.SystemBranding.Add(new SystemBrandingEntity
            {
                CompanyName = value.CompanyName,
                LogoBytes = value.LogoBytes,
                LogoContentType = value.LogoContentType,
                LogoSha256 = value.LogoSha256,
                Revision = 1,
            });
        }
        else
        {
            current.CompanyName = value.CompanyName;
            current.LogoBytes = value.LogoBytes;
            current.LogoContentType = value.LogoContentType;
            current.LogoSha256 = value.LogoSha256;
            current.Revision = expectedRevision + 1;
        }
        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "systemBranding",
            EntityId = "global",
            ActorId = actorId,
            Source = "approvalService",
            Kind = action,
            BeforeJson = before,
            AfterJson = AuditSnapshot(value, reason),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException) when (current is null &&
            db.SystemBranding.AsNoTracking().Any(item => item.Id == "global"))
        {
            // 两名管理员同时初始化空库时，把唯一键冲突返回为可刷新冲突。
            return false;
        }
    }

    /// <summary>
    /// 审计仅记名称和图片摘要，绝不复制原始图片字节。
    /// </summary>
    /// <param name="value">设置状态。</param>
    /// <param name="reason">管理员说明。</param>
    /// <returns>脱敏 JSON。</returns>
    private static string AuditSnapshot(SystemBrandingEntity value, string reason) =>
        JsonSerializer.Serialize(new
        {
            value.CompanyName,
            value.LogoSha256,
            reason
        });
}
