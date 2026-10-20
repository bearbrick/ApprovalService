namespace ApprovalService.Domain.Files;

/// <summary>
/// 附件的持久化元数据；文件内容始终保存在数据库之外。
/// </summary>
public sealed class StoredFileEntity
{
    /// <summary>
    /// 文件 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 上传时的文件名，仅用于显示和下载，不参与存储路径。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 实际上传字节数。
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
    /// 文件内容的 SHA-256 小写十六进制校验值。
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// 实际存储实现的编码；切换默认存储后仍可读取旧文件。
    /// </summary>
    public string StorageKind { get; set; } = string.Empty;

    /// <summary>
    /// 存储内部的对象键，不含用户传入的文件名。
    /// </summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>
    /// 引用该文件的审批单 ID；上传后尚未引用时为空。
    /// </summary>
    public string? InstanceId
    {
        get; set;
    }

    /// <summary>
    /// 已认证上传人的 ID。
    /// </summary>
    public string UploadedById { get; set; } = string.Empty;

    /// <summary>
    /// 上传完成时间。
    /// </summary>
    public DateTimeOffset UploadedAt
    {
        get; set;
    }

    /// <summary>
    /// 首次关联审批单的时间。
    /// </summary>
    public DateTimeOffset? LinkedAt
    {
        get; set;
    }

    /// <summary>
    /// 清理租约持有者；设置后文件不再允许关联审批单。
    /// </summary>
    public string? CleanupOwner
    {
        get; set;
    }

    /// <summary>
    /// 清理租约截止时间；中断后允许其他实例重试。
    /// </summary>
    public DateTimeOffset? CleanupLeaseUntil
    {
        get; set;
    }
}
