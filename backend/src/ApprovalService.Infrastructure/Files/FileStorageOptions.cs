namespace ApprovalService.Infrastructure.Files;

/// <summary>
/// 附件存储配置；密钥只从部署环境读取，不写入仓库。
/// </summary>
public sealed class FileStorageOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "ApprovalService:Files";

    /// <summary>
    /// 新上传文件的存储提供者：Local 或 Minio。
    /// </summary>
    public string Provider { get; set; } = "Local";

    /// <summary>
    /// 单个文件最大字节数，默认二十兆字节。
    /// </summary>
    public long MaxUploadBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>
    /// MinIO/S3 服务地址，不包含对象桶。
    /// </summary>
    public string? Endpoint
    {
        get; set;
    }

    /// <summary>
    /// MinIO/S3 桶名。
    /// </summary>
    public string? Bucket
    {
        get; set;
    }

    /// <summary>
    /// 对象存储访问标识。
    /// </summary>
    public string? AccessKey
    {
        get; set;
    }

    /// <summary>
    /// 对象存储访问密钥；只通过部署配置注入。
    /// </summary>
    public string? SecretKey
    {
        get; set;
    }

    /// <summary>
    /// 浏览器能否直接访问对象存储地址。
    /// </summary>
    public bool BrowserAccessible
    {
        get; set;
    }

    /// <summary>
    /// 对象存储预签名地址有效秒数。
    /// </summary>
    public int PresignedUrlSeconds { get; set; } = 300;
}
