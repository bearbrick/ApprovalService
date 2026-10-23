using System.Text.Json;
using ApprovalService.Application.Notifications;
using ApprovalService.Domain.Notifications.Calendar;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Notifications;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 验证期限提醒的持久去重、失效过滤和工作分钟提前量。
/// </summary>
internal static class NotificationDeadlineSchedulerChecks
{
    /// <summary>
    /// 执行期限提醒检查。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var calendar = new WorkCalendarRules([
            new WorkCalendarDayOverride(new DateOnly(2026, 10, 23), false),
            new WorkCalendarDayOverride(new DateOnly(2026, 10, 24), true),
        ]);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var hours = WorkCalendarWorkingHours.Default;
        var clock = new DeadlineReminderClock(calendar, zone, hours);
        var due = new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.FromHours(8));
        var before = new DateTimeOffset(2026, 10, 22, 17, 30, 0, TimeSpan.FromHours(8));
        check("到期前一工作小时跳过节假日并计入调休周末",
            clock.GetBeforeDueAt(due, 60) == before && calendar.GetDueAt(before, 60, zone, hours) == due);
        check("期限恰在开工时从前一工作时段向前累计",
            clock.GetBeforeDueAt(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(8)), 60) ==
            new DateTimeOffset(2026, 10, 24, 17, 0, 0, TimeSpan.FromHours(8)));

        var directory = TempDataDirectory.Create();
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "deadline-reminders.db")).Options;
            var factory = new ReminderContextFactory(options);
            await SeedAsync(factory, due);
            var now = before.AddMinutes(1);
            var time = new ReminderTimeProvider(now);
            var handler = new DeadlineReminderOutboxHandler(factory,
                new NotificationAppService(new NotificationRepository(factory)), time);
            var early = CreateItem("reminder-early", "beforeDue", due, before);
            await handler.DeliverAsync(early, CancellationToken.None);
            await using (var db = await factory.CreateDbContextAsync())
            {
                var messages = await db.OutboxItems.AsNoTracking().ToArrayAsync();
                check("快到期提醒给当前审批人和发起人各排站内及邮件，保留原定时刻",
                    messages.Length == 4 && messages.All(message => message.Kind == "notification") &&
                    messages.All(message =>
                        JsonSerializer.Deserialize<ApprovalService.Domain.Notifications.NotificationMessage>(
                            message.PayloadJson)?.CreatedAt == before));
            }

            // 模拟工作者发送后、确认源项前退出；重启重新执行不应多建子通知。
            var restarted = new DeadlineReminderOutboxHandler(factory,
                new NotificationAppService(new NotificationRepository(factory)), time);
            await restarted.DeliverAsync(early, CancellationToken.None);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("期限提醒在进程重启重试后按稳定 ID 去重", await db.OutboxItems.CountAsync() == 4);
            }

            time.Now = due.AddMinutes(1);
            await handler.DeliverAsync(CreateItem("reminder-late-early", "beforeDue", due, before),
                CancellationToken.None);
            await handler.DeliverAsync(CreateItem("reminder-overdue", "overdue", due, due),
                CancellationToken.None);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("宕机跨过到期点时丢弃过期的提前提醒，仅安排超时提醒",
                    await db.OutboxItems.CountAsync() == 8);
                var task = await db.ApprovalTasks.SingleAsync(candidate => candidate.Id == "task-deadline");
                task.Status = EngineTaskStatuses.Approved;
                await db.SaveChangesAsync();
            }

            await handler.DeliverAsync(CreateItem("reminder-completed", "overdue", due, due),
                CancellationToken.None);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("审批完成后到期的持久提醒不产生过时消息", await db.OutboxItems.CountAsync() == 8);
                var task = await db.ApprovalTasks.SingleAsync(candidate => candidate.Id == "task-deadline");
                task.Status = EngineTaskStatuses.Pending;
                task.DueAt = due.AddDays(1);
                await db.SaveChangesAsync();
            }

            await handler.DeliverAsync(CreateItem("reminder-replanned", "overdue", due, due),
                CancellationToken.None);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("待办期限重新安排后旧提醒不会发送", await db.OutboxItems.CountAsync() == 8);
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 准备两位有邮箱的参与者及一项待办。
    /// </summary>
    /// <param name="factory">测试数据库工厂。</param>
    /// <param name="due">待办期限。</param>
    /// <returns>异步操作。</returns>
    private static async Task SeedAsync(ReminderContextFactory factory, DateTimeOffset due)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.Flows.Add(new FlowEntity { Id = "flow-deadline", Code = "DEADLINE", Name = "期限审批" });
        db.FlowVersions.Add(new FlowVersionEntity
        {
            Id = "version-deadline",
            FlowId = "flow-deadline",
            Version = 1,
            DefinitionJson = "{}",
            GraphJson = "{}",
        });
        db.Instances.Add(new InstanceEntity
        {
            Id = "instance-deadline",
            SerialNo = "AP-DUE-2",
            FlowId = "flow-deadline",
            FlowVersionId = "version-deadline",
            FlowCode = "DEADLINE",
            BusinessKey = "B-DUE-2",
            Title = "采购申请",
            InitiatorId = "requester",
            ClientAppCode = "demo",
            Status = EngineInstanceStatuses.Running,
        });
        db.ApprovalTasks.Add(new ApprovalTaskEntity
        {
            Id = "task-deadline",
            InstanceId = "instance-deadline",
            NodeId = "approve",
            AssigneeId = "approver",
            Status = EngineTaskStatuses.Pending,
            DueAt = due,
        });
        db.People.AddRange(
            new PersonEntity { Id = "approver", EmployeeNumber = "A001", Name = "审批人", Email = "approver@example.test" },
            new PersonEntity { Id = "requester", EmployeeNumber = "R001", Name = "发起人", Email = "requester@example.test" });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 以应用层固定的字段建立延迟提醒待发项。
    /// </summary>
    /// <param name="id">待发项稳定 ID。</param>
    /// <param name="kind">提前或超时编码。</param>
    /// <param name="due">待办期限。</param>
    /// <param name="scheduled">原定提醒时间。</param>
    /// <returns>可供处理器读取的持久项。</returns>
    private static OutboxItemEntity CreateItem(string id, string kind, DateTimeOffset due,
        DateTimeOffset scheduled) => new()
        {
            Id = id,
            InstanceId = "instance-deadline",
            EventId = id,
            Kind = DeadlineReminderOutboxHandler.ReminderKind,
            AvailableAt = scheduled,
            PayloadJson = JsonSerializer.Serialize(new
            {
                TaskId = "task-deadline",
                DueAt = due,
                ReminderKind = kind,
                ScheduledAt = scheduled,
            }),
        };

    /// <summary>
    /// 为每次操作建立独立的 SQLite 上下文。
    /// </summary>
    /// <param name="options">共享测试库选项。</param>
    private sealed class ReminderContextFactory(DbContextOptions<ApprovalDbContext> options)
        : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 可设定到期前或到期后的检查时间。
    /// </summary>
    /// <param name="now">初始 UTC 时间。</param>
    private sealed class ReminderTimeProvider(DateTimeOffset now) : TimeProvider
    {
        /// <summary>
        /// 当前固定绝对时刻。
        /// </summary>
        public DateTimeOffset Now { get; set; } = now;

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
}
