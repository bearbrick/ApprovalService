namespace ApprovalService.Domain.Notifications;

/// <summary>
/// 单个人对一种提醒的邮件与站内渠道订阅；缺失记录表示两种渠道均开启。
/// </summary>
public sealed class NotificationPreferenceEntity
{
    /// <summary>
    /// 内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// 提醒类别。
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 是否接收邮件。
    /// </summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>
    /// 是否接收站内消息。
    /// </summary>
    public bool InAppEnabled { get; set; } = true;
}

/// <summary>
/// 管理员设置的每日待办汇总本地发送时刻。
/// </summary>
public sealed class DailyDigestSettingsEntity
{
    /// <summary>
    /// 全局设置主键。
    /// </summary>
    public string Id { get; set; } = "global";

    /// <summary>
    /// 24 小时制 HH:mm；空值表示停发。
    /// </summary>
    public string? LocalSendTime
    {
        get; set;
    }
}

/// <summary>
/// 通知规则使用的固定类别编码。
/// </summary>
public static class NotificationCategories
{
    /// <summary>
    /// 新待办。
    /// </summary>
    public const string NewTask = "newTask";

    /// <summary>
    /// 被催办。
    /// </summary>
    public const string Urge = "urge";

    /// <summary>
    /// 快到期。
    /// </summary>
    public const string BeforeDue = "beforeDue";

    /// <summary>
    /// 已超时。
    /// </summary>
    public const string Overdue = "overdue";

    /// <summary>
    /// 审批结果。
    /// </summary>
    public const string Result = "result";

    /// <summary>
    /// 被抄送。
    /// </summary>
    public const string Copy = "cc";

    /// <summary>
    /// 被提及。
    /// </summary>
    public const string Mention = "mention";

    /// <summary>
    /// 每日待办汇总邮件。
    /// </summary>
    public const string DailyDigest = "dailyDigest";

    /// <summary>
    /// 向个人设置界面提供全部可订阅类别。
    /// </summary>
    public static IReadOnlyList<string> All
    {
        get;
    } =
    [NewTask, Urge, BeforeDue, Overdue, Result, Copy, Mention, DailyDigest];
}
