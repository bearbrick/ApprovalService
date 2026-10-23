namespace ApprovalService.Domain.Notifications;

/// <summary>
/// 个人订阅和每日汇总时间的持久化边界。
/// </summary>
public interface INotificationSettingsRepository
{
    /// <summary>
    /// 读取一个人的显式订阅记录。
    /// </summary>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已保存的记录；缺失类别默认开启。</returns>
    Task<IReadOnlyList<NotificationPreferenceEntity>> ListAsync(string personId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取一种提醒的一个渠道是否开启。
    /// </summary>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="category">提醒类别。</param>
    /// <param name="channelCode">渠道编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>启用为真；没有显式记录时也为真。</returns>
    Task<bool> IsEnabledAsync(string personId, string category, string channelCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 覆盖一类提醒的渠道设置。
    /// </summary>
    /// <param name="preference">已校验的个人设置。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task SetAsync(NotificationPreferenceEntity preference, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取管理员配置的每日汇总本地时间。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>HH:mm；未配置时为空。</returns>
    Task<string?> GetDailyDigestTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置每日汇总本地时间，空值停发。
    /// </summary>
    /// <param name="localTime">HH:mm 或空值。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task SetDailyDigestTimeAsync(string? localTime, CancellationToken cancellationToken = default);
}
