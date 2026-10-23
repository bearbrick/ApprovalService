namespace ApprovalService.Domain.Notifications;

/// <summary>
/// 不含业务触发规则的待发送通知内容。
/// </summary>
public sealed class NotificationMessage
{
    /// <summary>
    /// 消息及重试幂等 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 来源审批单 ID；系统通知可为空。
    /// </summary>
    public string? InstanceId
    {
        get; set;
    }

    /// <summary>
    /// 来源事件 ID；没有业务事件时使用消息 ID。
    /// </summary>
    public string? EventId
    {
        get; set;
    }

    /// <summary>
    /// 渠道编码。
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 内部收件人员 ID。
    /// </summary>
    public string RecipientId { get; set; } = string.Empty;

    /// <summary>
    /// 外部收件地址，邮件渠道必填。
    /// </summary>
    public string? RecipientAddress
    {
        get; set;
    }

    /// <summary>
    /// 消息标题。
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// 纯文本正文，由后续业务模块生成。
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// 审批中心内需登录的相对路径，不包含免登录令牌。
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// 消息产生的 UTC 时间。
    /// </summary>
    public DateTimeOffset CreatedAt
    {
        get; set;
    }
}

/// <summary>
/// 已持久化的站内消息。
/// </summary>
public sealed class InAppNotificationEntity
{
    /// <summary>
    /// 原通知 ID，用于重试幂等。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 收件人员 ID。
    /// </summary>
    public string RecipientId { get; set; } = string.Empty;

    /// <summary>
    /// 消息标题。
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// 纯文本正文。
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// 审批中心内需登录的相对路径。
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// 消息产生的 UTC 时间。
    /// </summary>
    public DateTimeOffset CreatedAt
    {
        get; set;
    }

    /// <summary>
    /// 收件人确认已读的 UTC 时间。
    /// </summary>
    public DateTimeOffset? ReadAt
    {
        get; set;
    }
}

/// <summary>
/// 通知使用的内部渠道与待发项编码。
/// </summary>
public static class NotificationChannelCodes
{
    /// <summary>
    /// SMTP 邮件。
    /// </summary>
    public const string Email = "email";

    /// <summary>
    /// 站内消息。
    /// </summary>
    public const string InApp = "inApp";

    /// <summary>
    /// 持久待发队列中的通知处理器类型。
    /// </summary>
    public const string Outbox = "notification";
}
