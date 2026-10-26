using ApprovalService.Domain.Center;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Center;

/// <summary>
/// 三种数据库共用的委托规则映射。
/// </summary>
internal static class DelegationRuleModelConfiguration
{
    /// <summary>
    /// 配置规则主键、范围正文和时间查询索引。
    /// </summary>
    /// <param name="modelBuilder">数据库模型。</param>
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DelegationRuleEntity>(entity =>
        {
            entity.ToTable("DelegationRules");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(32);
            entity.Property(item => item.PrincipalId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.DelegateId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.FlowCodesJson).IsRequired();
            entity.Ignore(item => item.FlowCodes);
            entity.HasIndex(item => new { item.PrincipalId, item.StartsAtUtc, item.EndsAtUtc });
        });
    }
}
