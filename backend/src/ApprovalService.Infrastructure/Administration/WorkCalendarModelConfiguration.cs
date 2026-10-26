using ApprovalService.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Administration;

/// <summary>
/// 三库共用的日历快照及并发修订映射。
/// </summary>
internal static class WorkCalendarModelConfiguration
{
    /// <summary>
    /// 登记全局日历快照。
    /// </summary>
    /// <param name="builder">模型构造器。</param>
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<WorkCalendarEntity>(entity =>
        {
            entity.ToTable("WorkCalendar");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(16);
            entity.Property(item => item.Revision).IsConcurrencyToken();
            entity.Property(item => item.WorkingStart).HasMaxLength(5).IsRequired();
            entity.Property(item => item.WorkingEnd).HasMaxLength(5).IsRequired();
            entity.Property(item => item.OverridesJson).IsRequired();
        });
    }
}
