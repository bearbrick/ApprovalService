using ApprovalService.Domain.Center;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Center;

/// <summary>
/// 本人手写签名资产的三库统一映射。
/// </summary>
internal static class SignatureAssetModelConfiguration
{
    /// <summary>
    /// 将不可变 PNG 资产及人员索引加入数据库模型。
    /// </summary>
    /// <param name="modelBuilder">当前数据库模型。</param>
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SignatureAssetEntity>(entity =>
        {
            entity.ToTable("SignatureAssets");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.PersonId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.PngBytes).IsRequired();
            entity.Property(item => item.Sha256).HasMaxLength(64).IsRequired();
            entity.HasIndex(item => new { item.PersonId, item.IsPersonal, item.UploadedAt });
        });
    }
}
