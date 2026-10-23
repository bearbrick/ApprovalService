namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 一类提醒的邮件和站内开关。
/// </summary>
/// <param name="Category">提醒类别编码。</param>
/// <param name="EmailEnabled">是否接收邮件。</param>
/// <param name="InAppEnabled">是否接收站内消息。</param>
public sealed record NotificationPreferenceResponse(string Category, bool EmailEnabled, bool InAppEnabled);

/// <summary>
/// 修改本人一类提醒的渠道开关。
/// </summary>
/// <param name="EmailEnabled">是否接收邮件。</param>
/// <param name="InAppEnabled">是否接收站内消息。</param>
public sealed record UpdateNotificationPreferenceRequest(bool EmailEnabled, bool InAppEnabled);

/// <summary>
/// 每日待办汇总的当地发送时刻；空值表示停发。
/// </summary>
/// <param name="LocalTime">严格 HH:mm 或空值。</param>
public sealed record DailyDigestSettingsResponse(string? LocalTime);

/// <summary>
/// 管理员修改每日待办汇总的发送时刻。
/// </summary>
/// <param name="LocalTime">严格 HH:mm 或空值。</param>
public sealed record UpdateDailyDigestSettingsRequest(string? LocalTime);
