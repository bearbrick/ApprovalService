using ApprovalService.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Administration;

/// <summary>
/// 三种数据库共用的全局品牌设置表映射。
/// </summary>
internal static class SystemBrandingModelConfiguration
{
    /// <summary>
    /// 登记全局设置的字段与并发修订。
    /// </summary>
    /// <param name="modelBuilder">模型构造器。</param>
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemBrandingEntity>(entity =>
        {
            entity.ToTable("SystemBranding");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(16);
            entity.Property(item => item.CompanyName).HasMaxLength(60).IsRequired();
            entity.Property(item => item.LogoContentType).HasMaxLength(32);
            entity.Property(item => item.LogoSha256).HasMaxLength(64);
            entity.Property(item => item.Revision).IsConcurrencyToken();
        });
    }
}
