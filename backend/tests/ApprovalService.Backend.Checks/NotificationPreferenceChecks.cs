using ApprovalService.Application.Notifications;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Notifications;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 验证订阅持久化、管理员时间和每日汇总幂等。
/// </summary>
internal static class NotificationPreferenceChecks
{
    /// <summary>
    /// 运行个人设置和每日汇总检查。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "preferences.db")).Options;
            var factory = new PreferenceContextFactory(options);
            await SeedAsync(factory);
            var repository = new NotificationSettingsRepository(factory);
            var service = new NotificationSettingsAppService(repository);
            var defaults = await service.GetPreferencesAsync("alice");
            check("个人提醒缺省开启两种渠道，每日汇总仅有邮件渠道",
                defaults.Count == NotificationCategories.All.Count &&
                defaults.Single(item => item.Category == NotificationCategories.BeforeDue).EmailEnabled &&
                defaults.Single(item => item.Category == NotificationCategories.BeforeDue).InAppEnabled &&
                defaults.Single(item => item.Category == NotificationCategories.DailyDigest).EmailEnabled &&
                !defaults.Single(item => item.Category == NotificationCategories.DailyDigest).InAppEnabled);

            await service.SetPreferenceAsync("alice", NotificationCategories.DailyDigest, false, false);
            var restartedRepository = new NotificationSettingsRepository(factory);
            check("个人关闭每日汇总后跨仓储实例仍保留设置",
                !await restartedRepository.IsEnabledAsync("alice", NotificationCategories.DailyDigest,
                    NotificationChannelCodes.Email) &&
                (await service.GetPreferencesAsync("alice")).Single(item =>
                    item.Category == NotificationCategories.DailyDigest).EmailEnabled == false);
            check("管理员未配置时间时每日汇总默认停发", await service.GetDailyDigestTimeAsync() is null);

            var badTime = false;
            try
            {
                await service.SetDailyDigestTimeAsync("9:00");
            }
            catch (ApprovalService.Domain.Shared.UserFriendlyException)
            {
                badTime = true;
            }

            check("每日汇总发送时间只接受严格 HH:mm", badTime);
            var scheduler = new DailyDigestScheduler(factory, restartedRepository,
                new ConfigurationBuilder().AddInMemoryCollection().Build());
            var before = new DateTimeOffset(2026, 10, 23, 0, 59, 0, TimeSpan.Zero);
            check("管理员未设发送时间不建立持久任务", !await scheduler.ScheduleIfDueAsync(before));
            await service.SetDailyDigestTimeAsync("09:00");
            check("管理员设置的本地时刻在到点前不发", !await scheduler.ScheduleIfDueAsync(before));
            var due = before.AddMinutes(1);
            check("到点建立一次可重启的每日持久任务",
                await scheduler.ScheduleIfDueAsync(due) && !await scheduler.ScheduleIfDueAsync(due.AddMinutes(1)));

            ScheduledJobEntity job;
            await using (var db = await factory.CreateDbContextAsync())
            {
                job = await db.ScheduledJobs.AsNoTracking().SingleAsync();
                check("每日汇总任务保存在数据库并记录管理员当时的本地时间",
                    job.Kind == DailyDigestJobHandler.JobKind && job.PayloadJson.Contains("09:00", StringComparison.Ordinal));
            }

            var handler = new DailyDigestJobHandler(factory, restartedRepository,
                new NotificationAppService(new NotificationRepository(factory)));
            await handler.ExecuteAsync(job, CancellationToken.None);
            await using (var db = await factory.CreateDbContextAsync())
            {
                var messages = await db.OutboxItems.AsNoTracking().ToArrayAsync();
                check("每日汇总只向有待办且未关闭订阅的在职人员排邮件",
                    messages.Length == 1 && messages[0].PayloadJson.Contains("bob@example.test", StringComparison.Ordinal) &&
                    messages[0].PayloadJson.Contains("dailyDigest", StringComparison.Ordinal));
            }

            await service.SetPreferenceAsync("alice", NotificationCategories.DailyDigest, true, false);
            await handler.ExecuteAsync(job, CancellationToken.None);
            await handler.ExecuteAsync(job, CancellationToken.None);
            await using (var db = await factory.CreateDbContextAsync())
            {
                var messages = await db.OutboxItems.AsNoTracking().ToArrayAsync();
                check("汇总任务重试只为每人每天留一封且邮件包含待办摘要与登录链接",
                    messages.Length == 2 && messages.Any(item => item.PayloadJson.Contains("AP-1", StringComparison.Ordinal) &&
                        item.PayloadJson.Contains("/m/approvals/instance-1", StringComparison.Ordinal)));
            }

            await service.SetPreferenceAsync("alice", NotificationCategories.DailyDigest, false, false);
            await using (var db = await factory.CreateDbContextAsync())
            {
                var queued = (await db.OutboxItems.AsNoTracking().ToArrayAsync()).Single(item =>
                    item.PayloadJson.Contains("alice@example.test", StringComparison.Ordinal));
                var channel = new CountingChannel();
                await new NotificationOutboxHandler([channel], restartedRepository)
                    .DeliverAsync(queued, CancellationToken.None);
                check("邮件已排队后关闭订阅仍可阻止实际投递", channel.SendCount == 0);
            }

            await service.SetDailyDigestTimeAsync("09:30");
            check("管理员改发送时刻后旧任务失效且新时刻才建任务",
                !await scheduler.ScheduleIfDueAsync(due.AddMinutes(29)) &&
                await scheduler.ScheduleIfDueAsync(due.AddMinutes(30)));
            ScheduledJobEntity changedJob;
            await using (var db = await factory.CreateDbContextAsync())
            {
                changedJob = await db.ScheduledJobs.AsNoTracking().SingleAsync(item => item.Id != job.Id);
            }

            await handler.ExecuteAsync(job, CancellationToken.None);
            await handler.ExecuteAsync(changedJob, CancellationToken.None);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("管理员当天改时间也不会给同一人发第二封汇总邮件",
                    await db.OutboxItems.CountAsync() == 2);
            }

            await service.SetDailyDigestTimeAsync(null);
            check("管理员清空时间后停发并让当天旧任务失效",
                !await scheduler.ScheduleIfDueAsync(due.AddDays(1)) &&
                await service.GetDailyDigestTimeAsync() is null);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 准备审批单、待办与两位在职人员。
    /// </summary>
    /// <param name="factory">测试数据库工厂。</param>
    /// <returns>异步操作。</returns>
    private static async Task SeedAsync(PreferenceContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.Flows.Add(new FlowEntity { Id = "flow-1", Code = "FLOW-1", Name = "测试流程" });
        db.FlowVersions.Add(new FlowVersionEntity
        {
            Id = "version-1",
            FlowId = "flow-1",
            DefinitionJson = "{}",
            GraphJson = "{}",
            Version = 1,
        });
        db.Instances.Add(new InstanceEntity
        {
            Id = "instance-1",
            FlowId = "flow-1",
            FlowVersionId = "version-1",
            FlowCode = "FLOW-1",
            BusinessKey = "B-1",
            SerialNo = "AP-1",
            Title = "采购申请",
            InitiatorId = "owner",
            ClientAppCode = "demo",
            Status = EngineInstanceStatuses.Running,
        });
        db.ApprovalTasks.Add(new ApprovalTaskEntity
        {
            Id = "task-1",
            InstanceId = "instance-1",
            NodeId = "approve",
            AssigneeId = "alice",
            DelegatedForId = "bob",
            Status = EngineTaskStatuses.Pending,
        });
        db.People.AddRange(
            new PersonEntity { Id = "alice", EmployeeNumber = "A001", Name = "甲", Email = "alice@example.test" },
            new PersonEntity { Id = "bob", EmployeeNumber = "B001", Name = "乙", Email = "bob@example.test" });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 为检查创建独立的 SQLite 上下文。
    /// </summary>
    /// <param name="options">共享数据库选项。</param>
    private sealed class PreferenceContextFactory(DbContextOptions<ApprovalDbContext> options)
        : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 记录邮件渠道是否真正执行。
    /// </summary>
    private sealed class CountingChannel : INotificationChannel
    {
        /// <inheritdoc />
        public string Code => NotificationChannelCodes.Email;

        /// <summary>
        /// 已发送次数。
        /// </summary>
        public int SendCount
        {
            get; private set;
        }

        /// <inheritdoc />
        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }
}
