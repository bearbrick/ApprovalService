namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 人员打印与组织管理员字体管理接口。
/// </summary>
public static class PrintingRoutes
{
    /// <summary>
    /// 人员 JWT 获取审批单 PDF；每次成功生成计一次打印。
    /// </summary>
    public const string Instance = "/api/v1/instances/{id}/print";
    /// <summary>
    /// 本地组织管理员查询和上传字体。
    /// </summary>
    public const string Font = "/api/v1/admin/printing/font";
    /// <summary>
    /// 审批单审计中的打印事件种类。
    /// </summary>
    public const string PrintedEvent = "printed";
}

/// <summary>
/// 当前字体的安全摘要，不返回可再分发的字体内容。
/// </summary>
public sealed class PrintFontResponse
{
    /// <summary>
    /// 是否已经上传字体。
    /// </summary>
    public bool Configured
    {
        get; set;
    }
    /// <summary>
    /// 原始文件名。
    /// </summary>
    public string? FileName
    {
        get; set;
    }
    /// <summary>
    /// 字体内容的 SHA-256。
    /// </summary>
    public string? Sha256
    {
        get; set;
    }
    /// <summary>
    /// 上传时间。
    /// </summary>
    public DateTimeOffset? UploadedAt
    {
        get; set;
    }
}
