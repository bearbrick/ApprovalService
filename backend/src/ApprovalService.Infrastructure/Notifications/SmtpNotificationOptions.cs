namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// SMTP 连接与审批中心公开地址；密码从部署密钥配置读取，不写入仓库。
/// </summary>
public sealed class SmtpNotificationOptions
{
    /// <summary>
    /// 配置节路径。
    /// </summary>
    public const string SectionName = "ApprovalService:Notifications:Smtp";

    /// <summary>
    /// SMTP 主机名。
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// SMTP 端口。
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// 是否使用 STARTTLS；生产部署应开启。
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// SMTP 认证用户名；无需认证时为空。
    /// </summary>
    public string? UserName
    {
        get; set;
    }

    /// <summary>
    /// SMTP 认证密码；从环境变量或部署密钥提供。
    /// </summary>
    public string? Password
    {
        get; set;
    }

    /// <summary>
    /// 发件邮箱地址。
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// 审批中心对收件人可访问的 HTTP(S) 基础地址。
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
