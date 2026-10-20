namespace ApprovalService.Application.Files;

/// <summary>
/// 附件上传和短时下载地址的可配置限制。
/// </summary>
public sealed class FilePolicyOptions
{
    /// <summary>
    /// 每批处理的过期上传数；后台任务在满批次时继续下一批。
    /// </summary>
    public const int CleanupBatchSize = 100;

    /// <summary>
    /// 默认单文件二十兆字节。
    /// </summary>
    public long MaxUploadBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>
    /// 无审批单引用的附件保留时间。
    /// </summary>
    public TimeSpan UnusedRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 浏览器直连对象存储地址的有效时间。
    /// </summary>
    public TimeSpan DownloadUrlLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// 上传时由可信调用方提供的文件内容和认证身份。
/// </summary>
public sealed class UploadFileInput
{
    /// <summary>
    /// 上传文件流。
    /// </summary>
    public Stream Content { get; set; } = Stream.Null;

    /// <summary>
    /// 客户端显示文件名，不作为存储路径。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 已认证上传用户 ID。
    /// </summary>
    public string UploaderId { get; set; } = string.Empty;
}

/// <summary>
/// 上传成功后可用于审批数据引用的附件信息。
/// </summary>
public sealed class StoredFileDto
{
    /// <summary>
    /// 文件 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 显示文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 实际文件大小。
    /// </summary>
    public long Size
    {
        get; set;
    }

    /// <summary>
    /// 按字节识别的媒体类型。
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 小写十六进制值。
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// 下载准备结果：要么已写入输出流，要么给出短时直连地址。
/// </summary>
public sealed class FileDownloadDto
{
    /// <summary>
    /// 下载时的显示文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 响应的媒体类型。
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 文件字节数。
    /// </summary>
    public long Size
    {
        get; set;
    }

    /// <summary>
    /// 已验权后生成的短时直连地址；服务转发时为空。
    /// </summary>
    public Uri? DirectUrl
    {
        get; set;
    }
}
