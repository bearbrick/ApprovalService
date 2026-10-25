using ApprovalService.Domain.Center;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Center;

/// <summary>
/// 个人常用意见的数据库映射，供三种提供程序共享。
/// </summary>
internal static class CenterOpinionModelConfiguration
{
    /// <summary>
    /// 添加个人常用意见表及人员排序索引。
    /// </summary>
    /// <param name="modelBuilder">当前数据库模型。</param>
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CenterOpinionEntity>(entity =>
        {
            entity.ToTable("CenterOpinions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(32);
            entity.Property(item => item.PersonId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Content).HasMaxLength(500).IsRequired();
            entity.HasIndex(item => new { item.PersonId, item.CreatedAtUtc });
        });
    }
}
