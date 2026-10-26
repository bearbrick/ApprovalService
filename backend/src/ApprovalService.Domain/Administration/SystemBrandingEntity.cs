namespace ApprovalService.Domain.Administration;

/// <summary>
/// 供审批单打印页眉使用的全局单位名称与图片。
/// </summary>
public sealed class SystemBrandingEntity
{
    /// <summary>
    /// 全局设置的固定主键。
    /// </summary>
    public string Id { get; set; } = "global";

    /// <summary>
    /// 打印时显示的单位名称。
    /// </summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// 经过文件签名校验的 Logo 原始字节。
    /// </summary>
    public byte[]? LogoBytes
    {
        get; set;
    }

    /// <summary>
    /// 从文件签名判定的图片媒体类型。
    /// </summary>
    public string? LogoContentType
    {
        get; set;
    }

    /// <summary>
    /// 只用于展示与缓存刷新的内容摘要。
    /// </summary>
    public string? LogoSha256
    {
        get; set;
    }

    /// <summary>
    /// 乐观并发修订号。
    /// </summary>
    public long Revision
    {
        get; set;
    }
}
