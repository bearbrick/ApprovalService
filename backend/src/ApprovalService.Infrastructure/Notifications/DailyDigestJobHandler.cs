using System.Globalization;
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
/// 从持久任务读取当下的待办并为愿意接收的人排入每日汇总邮件。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
/// <param name="settings">最新个人及管理员设置。</param>
/// <param name="notifications">持久通知入队用例。</param>
public sealed class DailyDigestJobHandler(IDbContextFactory<ApprovalDbContext> factory,
    INotificationSettingsRepository settings, INotificationAppService notifications) : IScheduledJobHandler
{
    /// <summary>
    /// 每日待办汇总定时任务类型。
    /// </summary>
    public const string JobKind = "dailyDigest";

    /// <inheritdoc />
    public string Kind => JobKind;

    /// <inheritdoc />
    public async Task ExecuteAsync(ScheduledJobEntity job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var payload = JsonSerializer.Deserialize<DailyDigestJobPayload>(job.PayloadJson)
            ?? throw new InvalidOperationException("每日汇总任务内容为空。");
        if (!DateOnly.TryParseExact(payload.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ||
            !TimeOnly.TryParseExact(payload.LocalSendTime, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
        {
            throw new InvalidOperationException("每日汇总任务内容不完整。");
        }

        if (await settings.GetDailyDigestTimeAsync(cancellationToken) != payload.LocalSendTime)
        {
            // 管理员停发或改时刻后，旧排程不会误发；新时刻会建立新任务。
            return;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var pending = await (from task in db.ApprovalTasks.AsNoTracking()
                             join instance in db.Instances.AsNoTracking() on task.InstanceId equals instance.Id
                             where task.Status == EngineTaskStatuses.Pending && instance.Status == EngineInstanceStatuses.Running
                             select new
                             {
                                 TaskId = task.Id,
                                 task.AssigneeId,
                                 task.DelegatedForId,
                                 InstanceId = instance.Id,
                                 instance.SerialNo,
                                 instance.Title,
                             }).ToArrayAsync(cancellationToken);
        if (pending.Length == 0)
        {
            return;
        }

        var recipientIds = pending.SelectMany(item => new[] { item.AssigneeId, item.DelegatedForId })
            .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!)
            .Distinct(StringComparer.Ordinal).ToArray();
        var people = await db.People.AsNoTracking().Where(person => recipientIds.Contains(person.Id) &&
            person.IsActive && person.Email != null).ToArrayAsync(cancellationToken);
        foreach (var person in people)
        {
            if (string.IsNullOrWhiteSpace(person.Email) ||
                !MailAddress.TryCreate(person.Email, out var address) || address.Address != person.Email ||
                !await settings.IsEnabledAsync(person.Id, NotificationCategories.DailyDigest,
                    NotificationChannelCodes.Email, cancellationToken))
            {
                continue;
            }

            var tasks = pending.Where(item => item.AssigneeId == person.Id || item.DelegatedForId == person.Id)
                .OrderBy(item => item.SerialNo, StringComparer.Ordinal).ThenBy(item => item.TaskId, StringComparer.Ordinal)
                .ToArray();
            var messageId = CreateMessageId(date, person.Id);
            if (await db.OutboxItems.AsNoTracking().AnyAsync(item => item.Id == messageId, cancellationToken))
            {
                // 同一天改发送时间、重启或租约重领均只给此人留一封。
                continue;
            }

            var lines = tasks.Take(50).Select(item => $"- {item.SerialNo} {item.Title}").ToArray();
            var body = $"您有 {tasks.Length} 项待处理审批：\n" + string.Join("\n", lines);
            if (tasks.Length > lines.Length)
            {
                body += $"\n另有 {tasks.Length - lines.Length} 项，请登录审批中心查看。";
            }

            await notifications.QueueAsync(new NotificationMessage
            {
                Id = messageId,
                ChannelCode = NotificationChannelCodes.Email,
                Category = NotificationCategories.DailyDigest,
                RecipientId = person.Id,
                RecipientAddress = person.Email,
                Subject = $"待办汇总：{tasks.Length} 项待处理审批",
                Body = body.Length <= 20000 ? body : body[..20000],
                TargetPath = "/m/approvals/" + Uri.EscapeDataString(tasks[0].InstanceId),
                CreatedAt = job.DueAt,
            }, cancellationToken);
        }
    }

    /// <summary>
    /// 只从日期和内部人员 ID 计算固定长度邮件 ID。
    /// </summary>
    /// <param name="date">管理员时区中的日期。</param>
    /// <param name="personId">内部人员 ID。</param>
    /// <returns>当天该人员的唯一邮件 ID。</returns>
    private static string CreateMessageId(DateOnly date, string personId) =>
        "digest-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(personId)))[..32].ToLowerInvariant();
}
