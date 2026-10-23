using ApprovalService.Domain.Runtime;

namespace ApprovalService.Domain.Notifications;

/// <summary>
/// 通知待发项与站内收件箱的持久化边界。
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// 将通知待发项写入可重启的数据库队列。
    /// </summary>
    /// <param name="item">待发项。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task EnqueueAsync(OutboxItemEntity item, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按原通知 ID 幂等保存站内消息。
    /// </summary>
    /// <param name="message">待保存内容。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task SaveInAppAsync(NotificationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按人员读取最近的站内消息。
    /// </summary>
    /// <param name="recipientId">已认证的内部人员 ID。</param>
    /// <param name="take">最多读取的数量。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>按产生时间倒序的消息。</returns>
    Task<IReadOnlyList<InAppNotificationEntity>> ListAsync(string recipientId, int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅允许收件人员确认自己的消息已读。
    /// </summary>
    /// <param name="id">消息 ID。</param>
    /// <param name="recipientId">已认证的内部人员 ID。</param>
    /// <param name="readAt">已读 UTC 时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>消息存在且属于此人员为真。</returns>
    Task<bool> MarkReadAsync(string id, string recipientId, DateTimeOffset readAt,
        CancellationToken cancellationToken = default);
}
