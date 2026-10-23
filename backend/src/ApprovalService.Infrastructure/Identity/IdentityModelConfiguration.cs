using ApprovalService.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Identity;

/// <summary>
/// 本地账号、刷新令牌和一次性嵌入令牌的数据库配置。
/// </summary>
internal static class IdentityModelConfiguration
{
    /// <summary>
    /// 配置令牌摘要和人员关联。
    /// </summary>
    /// <param name="modelBuilder">统一模型构造器。</param>
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalAccountEntity>(entity =>
        {
            entity.ToTable("LocalAccounts");
            entity.HasKey(item => item.PersonId);
            entity.Property(item => item.PersonId).HasMaxLength(64);
            entity.Property(item => item.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(item => item.PasswordSalt).HasMaxLength(128).IsRequired();
            entity.Property(item => item.TokenVersion).IsConcurrencyToken();
            entity.HasIndex(item => item.LockedUntil);
        });

        modelBuilder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(item => item.TokenHash);
            entity.Property(item => item.TokenHash).HasMaxLength(64);
            entity.Property(item => item.PersonId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ClientAppCode).HasMaxLength(128);
            entity.Property(item => item.ExternalUserId).HasMaxLength(256);
            entity.Property(item => item.AuthGeneration);
            entity.HasIndex(item => new { item.PersonId, item.ExpiresAt });
        });

        modelBuilder.Entity<EmbedTokenEntity>(entity =>
        {
            entity.ToTable("EmbedTokens");
            entity.HasKey(item => item.TokenHash);
            entity.Property(item => item.TokenHash).HasMaxLength(64);
            entity.Property(item => item.ClientAppCode).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ExternalUserId).HasMaxLength(256).IsRequired();
            entity.Property(item => item.PersonId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.TokenVersion).IsRequired();
            entity.Property(item => item.AuthGeneration);
            entity.Property(item => item.TargetPath).HasMaxLength(1024).IsRequired();
            entity.HasIndex(item => new { item.PersonId, item.ExpiresAt });
        });
    }
}
