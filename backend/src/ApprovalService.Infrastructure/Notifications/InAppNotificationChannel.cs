using ApprovalService.Application.Notifications;
using ApprovalService.Domain.Notifications;

namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// 站内消息渠道，重试时用通知 ID 去重。
/// </summary>
/// <param name="repository">站内消息仓储。</param>
public sealed class InAppNotificationChannel(INotificationRepository repository) : INotificationChannel
{
    /// <inheritdoc />
    public string Code => NotificationChannelCodes.InApp;

    /// <inheritdoc />
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken) =>
        repository.SaveInAppAsync(message, cancellationToken);
}
