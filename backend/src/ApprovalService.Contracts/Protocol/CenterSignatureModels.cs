namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 签名资产的安全元数据，不在 JSON 响应中传输图片字节。
/// </summary>
public sealed class CenterSignatureResponse
{
    /// <summary>
    /// 不可变签名资产 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 图片 SHA-256 小写十六进制摘要。
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// 是否列入本人的可重复选择签名。
    /// </summary>
    public bool IsPersonal
    {
        get; set;
    }

    /// <summary>
    /// 上传时间。
    /// </summary>
    public DateTimeOffset UploadedAt
    {
        get; set;
    }
}
