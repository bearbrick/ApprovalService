using ApprovalService.Domain.Notifications;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 供后续业务事件模块使用的通知队列与收件箱用例。
/// </summary>
public interface INotificationAppService
{
    /// <summary>
    /// 排入持久待发队列，不在调用事务中发送邮件。
    /// </summary>
    /// <param name="message">已由业务模块构造的通知内容。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task QueueAsync(NotificationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按已认证人员读取收件箱。
    /// </summary>
    /// <param name="recipientId">内部人员 ID。</param>
    /// <param name="take">最多读取条数。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>该人员最近的站内消息。</returns>
    Task<IReadOnlyList<InAppNotificationEntity>> ListAsync(string recipientId, int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 确认该人员的一条站内消息已读。
    /// </summary>
    /// <param name="id">消息 ID。</param>
    /// <param name="recipientId">内部人员 ID。</param>
    /// <param name="readAt">已读 UTC 时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>属于该人员且存在为真。</returns>
    Task<bool> MarkReadAsync(string id, string recipientId, DateTimeOffset readAt,
        CancellationToken cancellationToken = default);
}
