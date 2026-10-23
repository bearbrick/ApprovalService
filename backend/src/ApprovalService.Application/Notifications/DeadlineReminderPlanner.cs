using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 延迟待发项内固定的提醒任务和原计划时间。
/// </summary>
public sealed class DeadlineReminderPayload
{
    /// <summary>
    /// 被提醒的任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 排期时固定的任务期限。
    /// </summary>
    public DateTimeOffset DueAt
    {
        get; set;
    }

    /// <summary>
    /// 到期前或超时提醒种类。
    /// </summary>
    public string ReminderKind { get; set; } = string.Empty;

    /// <summary>
    /// 原始计划触发时间；重试退避不改变消息时间。
    /// </summary>
    public DateTimeOffset ScheduledAt
    {
        get; set;
    }
}

/// <summary>
/// 在审批命令事务中为刚进入待办的任务固定两条期限提醒。
/// </summary>
public sealed class DeadlineReminderPlanner
{
    /// <summary>
    /// 延迟待发队列的处理器类型。
    /// </summary>
    public const string OutboxKind = "deadlineReminder";

    /// <summary>
    /// 到期前一个工作小时。
    /// </summary>
    public const string BeforeDue = "beforeDue";

    /// <summary>
    /// 任务到期时。
    /// </summary>
    public const string Overdue = "overdue";

    /// <summary>
    /// 按同一工作日历计算提前量。
    /// </summary>
    private readonly TaskDeadlinePolicy deadlinePolicy;

    /// <summary>
    /// 建立固定日历的提醒排期器。
    /// </summary>
    /// <param name="deadlinePolicy">任务期限和提前量使用的日历。</param>
    public DeadlineReminderPlanner(TaskDeadlinePolicy deadlinePolicy) =>
        this.deadlinePolicy = deadlinePolicy ?? throw new ArgumentNullException(nameof(deadlinePolicy));

    /// <summary>
    /// 仅对新建或排队转待办且有期限的任务创建持久待发项。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="tasks">命令后的任务投影。</param>
    /// <param name="previousStatuses">命令前的任务状态。</param>
    /// <param name="occurredAt">本次命令固定时间。</param>
    /// <returns>与审批事务一起提交的延迟待发项。</returns>
    public IReadOnlyList<OutboxItemEntity> Plan(string instanceId,
        IReadOnlyList<ApprovalTaskEntity> tasks, IReadOnlyDictionary<string, string> previousStatuses,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(previousStatuses);
        var items = new List<OutboxItemEntity>();
        foreach (var task in tasks)
        {
            if (task.Status != EngineTaskStatuses.Pending || task.DueAt is null ||
                previousStatuses.TryGetValue(task.Id, out var previous) && previous == EngineTaskStatuses.Pending)
            {
                continue;
            }

            var dueAt = task.DueAt.Value;
            var beforeAt = deadlinePolicy.CalculateReminderAt(dueAt);
            // 短于一工作小时的期限应在待办创建后尽快提醒，不能排到创建之前。
            items.Add(Create(instanceId, task.Id, dueAt, BeforeDue,
                beforeAt > occurredAt ? beforeAt : occurredAt));
            items.Add(Create(instanceId, task.Id, dueAt, Overdue,
                dueAt > occurredAt ? dueAt : occurredAt));
        }

        return items;
    }

    /// <summary>
    /// 使用任务、期限和种类生成固定 ID，使重新激活时的新期限有独立排期。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="dueAt">期限。</param>
    /// <param name="kind">提醒种类。</param>
    /// <param name="scheduledAt">原始计划发送时间。</param>
    /// <returns>可持久重试的待发项。</returns>
    private static OutboxItemEntity Create(string instanceId, string taskId, DateTimeOffset dueAt,
        string kind, DateTimeOffset scheduledAt)
    {
        var source = Encoding.UTF8.GetBytes(instanceId + "\n" + taskId + "\n" + dueAt.UtcTicks + "\n" + kind);
        var id = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var payload = new DeadlineReminderPayload
        {
            TaskId = taskId,
            DueAt = dueAt,
            ReminderKind = kind,
            ScheduledAt = scheduledAt,
        };
        return new OutboxItemEntity
        {
            Id = id,
            InstanceId = instanceId,
            EventId = id,
            Kind = OutboxKind,
            PayloadJson = JsonSerializer.Serialize(payload),
            AvailableAt = scheduledAt,
        };
    }
}
