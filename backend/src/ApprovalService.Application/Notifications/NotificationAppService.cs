using System.Net.Mail;
using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 校验通知传输边界，并把发送交给可重启的待发队列。
/// </summary>
/// <param name="repository">持久化仓储。</param>
public sealed class NotificationAppService(INotificationRepository repository) : INotificationAppService
{
    /// <inheritdoc />
    public async Task QueueAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        Validate(message);
        await repository.EnqueueAsync(new OutboxItemEntity
        {
            Id = message.Id,
            InstanceId = message.InstanceId ?? string.Empty,
            EventId = message.EventId ?? message.Id,
            Kind = NotificationChannelCodes.Outbox,
            PayloadJson = JsonSerializer.Serialize(message),
            AvailableAt = message.CreatedAt,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InAppNotificationEntity>> ListAsync(string recipientId, int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientId);
        if (take is < 1 or > 100)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "单次最多读取 100 条站内消息。");
        }

        return repository.ListAsync(recipientId, take, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> MarkReadAsync(string id, string recipientId, DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientId);
        return repository.MarkReadAsync(id, recipientId, readAt, cancellationToken);
    }

    /// <summary>
    /// 拒绝无效地址、过长内容和站外跳转，避免通知渠道把输入当作免登录链接发送。
    /// </summary>
    /// <param name="message">待校验内容。</param>
    private static void Validate(NotificationMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Id) || message.Id.Length > 64 || !ValidId(message.Id) ||
            message.InstanceId?.Length > 64 || message.EventId?.Length > 64 ||
            string.IsNullOrWhiteSpace(message.RecipientId) || message.RecipientId.Length > 128 ||
            string.IsNullOrWhiteSpace(message.Subject) || message.Subject.Length > 256 ||
            string.IsNullOrWhiteSpace(message.Body) || message.Body.Length > 20000 ||
            string.IsNullOrWhiteSpace(message.ChannelCode) || message.ChannelCode.Length > 64 ||
            message.CreatedAt == default)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "通知内容或渠道不正确。");
        }

        if (message.TargetPath.Length > 512 || !message.TargetPath.StartsWith("/", StringComparison.Ordinal) ||
            message.TargetPath.StartsWith("//", StringComparison.Ordinal) ||
            message.TargetPath.IndexOfAny(['\\', '\r', '\n']) >= 0)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "通知链接必须是审批中心的站内路径。");
        }

        if (message.ChannelCode == NotificationChannelCodes.Email &&
            (string.IsNullOrWhiteSpace(message.RecipientAddress) || message.RecipientAddress.Length > 320 ||
                !MailAddress.TryCreate(message.RecipientAddress, out var address) ||
                address.Address != message.RecipientAddress))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "邮件通知缺少有效收件地址。");
        }
    }

    /// <summary>
    /// 消息 ID 会进入邮件头与待发主键，只允许安全的 ASCII 标识字符。
    /// </summary>
    /// <param name="id">消息 ID。</param>
    /// <returns>所有字符均安全为真。</returns>
    private static bool ValidId(string id) => id.All(character =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or ':');
}
