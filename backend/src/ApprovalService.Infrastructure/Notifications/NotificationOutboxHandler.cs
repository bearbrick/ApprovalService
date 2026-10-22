using System.Text.Json;
using ApprovalService.Application.Notifications;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Jobs;

namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// 从持久待发项恢复通知内容，交给已登记的唯一渠道实现。
/// </summary>
/// <param name="channels">所有通知渠道。</param>
public sealed class NotificationOutboxHandler(IEnumerable<INotificationChannel> channels) : IOutboxHandler
{
    /// <inheritdoc />
    public string Kind => NotificationChannelCodes.Outbox;

    /// <inheritdoc />
    public async Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<NotificationMessage>(item.PayloadJson)
            ?? throw new InvalidOperationException("待发通知内容为空。");
        if (message.Id != item.Id)
        {
            throw new InvalidOperationException("待发通知 ID 与消息内容不一致。");
        }

        var matches = channels.Where(channel => channel.Code == message.ChannelCode).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("通知渠道未登记或编码重复。");
        }

        await matches[0].SendAsync(message, cancellationToken);
    }
}
