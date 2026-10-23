namespace ApprovalService.Application.Notifications;

/// <summary>
/// 人员订阅和管理员每日待办汇总时间的用例。
/// </summary>
public interface INotificationSettingsAppService
{
    /// <summary>
    /// 读取个人所有提醒类别的有效设置。
    /// </summary>
    /// <param name="personId">已认证内部人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>每类提醒的有效开关。</returns>
    Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(string personId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改本人一种提醒的渠道开关。
    /// </summary>
    /// <param name="personId">已认证内部人员 ID。</param>
    /// <param name="category">提醒类别。</param>
    /// <param name="emailEnabled">邮件是否开启。</param>
    /// <param name="inAppEnabled">站内消息是否开启。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>保存后的设置。</returns>
    Task<NotificationPreferenceDto> SetPreferenceAsync(string personId, string category,
        bool emailEnabled, bool inAppEnabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取管理员配置的每日汇总时间。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>HH:mm；未设置为空。</returns>
    Task<string?> GetDailyDigestTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改每日汇总时间，空值表示停发。
    /// </summary>
    /// <param name="localTime">严格 HH:mm 或空值。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>保存后的时间。</returns>
    Task<string?> SetDailyDigestTimeAsync(string? localTime, CancellationToken cancellationToken = default);
}

/// <summary>
/// 展示一个人对一种提醒的有效渠道设置。
/// </summary>
public sealed class NotificationPreferenceDto
{
    /// <summary>
    /// 提醒类别编码。
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 邮件是否开启。
    /// </summary>
    public bool EmailEnabled
    {
        get; set;
    }

    /// <summary>
    /// 站内消息是否开启。
    /// </summary>
    public bool InAppEnabled
    {
        get; set;
    }
}
