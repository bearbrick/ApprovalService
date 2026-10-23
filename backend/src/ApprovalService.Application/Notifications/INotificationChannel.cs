using ApprovalService.Domain.Notifications;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 可替换的通知传输渠道；增加渠道时不修改引擎核心。
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// 与通知内容一致的唯一渠道编码。
    /// </summary>
    string Code
    {
        get;
    }

    /// <summary>
    /// 在数据库事务之外发送或保存一条通知；站内渠道按消息 ID 幂等，SMTP 保留稳定消息 ID 供下游去重。
    /// </summary>
    /// <param name="message">通知内容。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}
