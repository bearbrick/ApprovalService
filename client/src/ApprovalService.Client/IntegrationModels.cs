namespace ApprovalService.Client;

/// <summary>
/// 一次性嵌入令牌及其有效期。
/// </summary>
public sealed class ApprovalEmbedToken
{
    /// <summary>
    /// 一次性随机令牌。
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 失效时间。
    /// </summary>
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }

    /// <summary>
    /// 允许打开的站内相对路径。
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;
}

/// <summary>
/// 上传后可在业务审批数据中引用的附件资料。
/// </summary>
public sealed class ApprovalUploadedFile
{
    /// <summary>
    /// 文件 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 安全显示名称。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件字节数。
    /// </summary>
    public long Size
    {
        get; set;
    }

    /// <summary>
    /// 按内容识别的媒体类型。
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 小写十六进制 SHA-256。
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// 已授权下载的附件内容与响应元数据。
/// </summary>
public sealed class ApprovalDownloadedFile
{
    /// <summary>
    /// 文件内容。
    /// </summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 服务返回的媒体类型。
    /// </summary>
    public string? ContentType
    {
        get; set;
    }

    /// <summary>
    /// 服务返回的文件名称。
    /// </summary>
    public string? FileName
    {
        get; set;
    }
}
