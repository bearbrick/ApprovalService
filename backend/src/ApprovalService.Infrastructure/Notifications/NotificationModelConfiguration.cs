using ApprovalService.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// 三种数据库共用的站内消息表配置。
/// </summary>
internal static class NotificationModelConfiguration
{
    /// <summary>
    /// 登记站内消息主键、收件索引和列宽。
    /// </summary>
    /// <param name="modelBuilder">统一模型构造器。</param>
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InAppNotificationEntity>(entity =>
        {
            entity.ToTable("InAppNotifications");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.RecipientId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Subject).HasMaxLength(256).IsRequired();
            entity.Property(item => item.Body).IsRequired();
            entity.Property(item => item.TargetPath).HasMaxLength(512).IsRequired();
            entity.HasIndex(item => new { item.RecipientId, item.CreatedAt });
        });
    }
}
