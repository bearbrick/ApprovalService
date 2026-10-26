namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 管理员可查看的打印页眉品牌设置，不含原始图片字节。
/// </summary>
public sealed class SystemBrandingResponse
{
    /// <summary>
    /// 单位名称。
    /// </summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// 当前 Logo 的内容摘要；未上传时为空。
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

/// <summary>
/// 修改单位名称的管理请求。
/// </summary>
public sealed class UpdateSystemBrandingRequest
{
    /// <summary>
    /// 单位名称。
    /// </summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// 编辑时读取的修订号。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 操作原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 用 Base64 图片内容更换打印页眉 Logo。
/// </summary>
public sealed class UploadSystemLogoRequest
{
    /// <summary>
    /// PNG 或 JPEG 图片的 Base64 正文。
    /// </summary>
    public string ContentBase64 { get; set; } = string.Empty;

    /// <summary>
    /// 编辑时读取的修订号。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 操作原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 移除打印页眉 Logo 的管理请求。
/// </summary>
public sealed class DeleteSystemLogoRequest
{
    /// <summary>
    /// 编辑时读取的修订号。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 操作原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
