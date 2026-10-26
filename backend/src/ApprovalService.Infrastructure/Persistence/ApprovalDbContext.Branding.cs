using ApprovalService.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 全局打印页眉设置的持久实体入口。
/// </summary>
public partial class ApprovalDbContext
{
    /// <summary>
    /// 打印页眉使用的全局单位品牌设置。
    /// </summary>
    public DbSet<SystemBrandingEntity> SystemBranding => Set<SystemBrandingEntity>();
}
