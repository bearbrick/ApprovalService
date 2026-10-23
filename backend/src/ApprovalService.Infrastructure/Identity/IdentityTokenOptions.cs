namespace ApprovalService.Infrastructure.Identity;

/// <summary>
/// 访问令牌的签名配置。
/// </summary>
public sealed class IdentityTokenOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "ApprovalService:Identity";

    /// <summary>
    /// 可选的 Base64 签名密钥；为空时从数据目录读取或生成。
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;
}
