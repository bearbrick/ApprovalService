using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Application.Notifications;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// 从延迟待发项恢复期限提醒，发送前核对待办仍有效。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
/// <param name="notifications">通知入队用例。</param>
/// <param name="timeProvider">可替换的时钟。</param>
public sealed class DeadlineReminderOutboxHandler(IDbContextFactory<ApprovalDbContext> factory,
    INotificationAppService notifications, TimeProvider timeProvider) : IOutboxHandler
{
    /// <summary>
    /// 延迟待发项的类型编码。
    /// </summary>
    public const string ReminderKind = DeadlineReminderPlanner.OutboxKind;

    /// <inheritdoc />
    public string Kind => ReminderKind;

    /// <inheritdoc />
    public async Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var payload = JsonSerializer.Deserialize<DeadlineReminderPayload>(item.PayloadJson)
            ?? throw new InvalidOperationException("期限提醒内容为空。");
        if (string.IsNullOrWhiteSpace(payload.TaskId) || payload.DueAt == default ||
            payload.ScheduledAt == default || payload.ReminderKind is not (DeadlineReminderPlanner.BeforeDue or
                DeadlineReminderPlanner.Overdue) ||
            string.IsNullOrWhiteSpace(item.InstanceId))
        {
            throw new InvalidOperationException("期限提醒内容不完整。");
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var task = await db.ApprovalTasks.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == payload.TaskId && candidate.InstanceId == item.InstanceId, cancellationToken);
        if (task is null || task.Status != EngineTaskStatuses.Pending || task.DueAt != payload.DueAt)
        {
            // 审批已处理、撤销或重排期时，旧时钟提醒成为无害的已完成待发项。
            return;
        }

        var instance = await db.Instances.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == item.InstanceId, cancellationToken);
        if (instance is null || instance.Status != EngineInstanceStatuses.Running)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (payload.ReminderKind == DeadlineReminderPlanner.BeforeDue && now >= payload.DueAt ||
            payload.ReminderKind == DeadlineReminderPlanner.Overdue && now < payload.DueAt)
        {
            // 宕机跨过到期点后只保留超时提醒，避免同时发两封过期的“即将到期”。
            return;
        }

        var recipientIds = new[] { task.AssigneeId, task.DelegatedForId, instance.InitiatorId }
            .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!)
            .Distinct(StringComparer.Ordinal).ToArray();
        var people = await db.People.AsNoTracking().Where(person => recipientIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, cancellationToken);
        var subject = payload.ReminderKind == DeadlineReminderPlanner.BeforeDue ? "审批任务即将到期" : "审批任务已超时";
        var body = payload.ReminderKind == DeadlineReminderPlanner.BeforeDue
            ? $"审批单 {instance.SerialNo}（{instance.Title}）有待办即将到期，请及时处理。"
            : $"审批单 {instance.SerialNo}（{instance.Title}）有待办已超时，请及时处理。";
        var path = "/m/approvals/" + Uri.EscapeDataString(instance.Id);
        foreach (var recipientId in recipientIds)
        {
            // 个人订阅接入时在这里筛选渠道，站内消息目前对所有参与者开启。
            await QueueIfAbsentAsync(db, item, payload, recipientId, NotificationChannelCodes.InApp,
                null, subject, body, path, cancellationToken);
            if (people.TryGetValue(recipientId, out var person) && person.IsActive &&
                !string.IsNullOrWhiteSpace(person.Email) && MailAddress.TryCreate(person.Email, out var address) &&
                address.Address == person.Email)
            {
                await QueueIfAbsentAsync(db, item, payload, recipientId, NotificationChannelCodes.Email,
                    person.Email, subject, body, path, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 每个提醒、收件人和渠道只创建一条稳定通知；跨重启重试不改写已入队内容。
    /// </summary>
    /// <param name="db">本次查询上下文。</param>
    /// <param name="item">源待发项。</param>
    /// <param name="payload">固定提醒内容。</param>
    /// <param name="recipientId">内部收件人员 ID。</param>
    /// <param name="channel">消息渠道。</param>
    /// <param name="address">邮件地址，站内消息为空。</param>
    /// <param name="subject">标题。</param>
    /// <param name="body">正文。</param>
    /// <param name="path">需登录的审批中心路径。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    private async Task QueueIfAbsentAsync(ApprovalDbContext db, OutboxItemEntity item,
        DeadlineReminderPayload payload, string recipientId, string channel, string? address,
        string subject, string body, string path, CancellationToken cancellationToken)
    {
        var id = NotificationId(item.Id, recipientId, channel);
        if (await db.OutboxItems.AsNoTracking().AnyAsync(existing => existing.Id == id, cancellationToken))
        {
            return;
        }

        await notifications.QueueAsync(new NotificationMessage
        {
            Id = id,
            InstanceId = item.InstanceId,
            EventId = item.EventId,
            ChannelCode = channel,
            RecipientId = recipientId,
            RecipientAddress = address,
            Subject = subject,
            Body = body,
            TargetPath = path,
            CreatedAt = payload.ScheduledAt,
            Category = payload.ReminderKind == DeadlineReminderPlanner.BeforeDue
                ? NotificationCategories.BeforeDue : NotificationCategories.Overdue,
        }, cancellationToken);
    }

    /// <summary>
    /// 从稳定来源计算受主键长度限制的通知 ID。
    /// </summary>
    /// <param name="sourceId">期限提醒待发项 ID。</param>
    /// <param name="recipientId">内部收件人员 ID。</param>
    /// <param name="channel">渠道编码。</param>
    /// <returns>只包含安全十六进制字符的通知 ID。</returns>
    private static string NotificationId(string sourceId, string recipientId, string channel) =>
        "dr-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            sourceId + ":" + recipientId + ":" + channel)))[..32].ToLowerInvariant();

}
