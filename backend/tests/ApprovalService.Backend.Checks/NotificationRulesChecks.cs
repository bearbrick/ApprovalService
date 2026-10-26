using System.Text.Json;
using ApprovalService.Application.Administration;
using ApprovalService.Application.Notifications;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Administration;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用工作日历和真实 SQLite 核对任务开始时限及旧期限保留。
/// </summary>
internal static class NotificationRulesChecks
{
    /// <summary>
    /// 运行期限配置、节假日和任务投影场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApprovalService:Notifications:Calendar:TimeZoneId"] = "Asia/Shanghai",
            ["ApprovalService:Notifications:Calendar:WorkingStart"] = "09:00",
            ["ApprovalService:Notifications:Calendar:WorkingEnd"] = "18:00",
            ["ApprovalService:Notifications:Calendar:Overrides:2026-10-23"] = "false",
            ["ApprovalService:Notifications:Calendar:Overrides:2026-10-24"] = "true",
        }).Build();
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
        graph.Nodes.Single(node => node.Id == "n1").Approval = new FlowApprovalSettings { DueMinutes = 60 };
        graph.Nodes.Single(node => node.Id == "n2").Approval = new FlowApprovalSettings { DueMinutes = 60 };
        graph.Nodes.Single(node => node.Id == "n1").Assignee = new FlowAssignee
        {
            Source = FlowAssigneeSources.Member,
            Value = "user-n1,user-next",
        };
        var firstTask = new EngineTask { Id = "first", NodeId = "n1" };
        var configuredDue = NotificationCalendarSettings.Create(configured).Calculate(graph, firstTask,
            new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.Zero));
        check("节点期限跳过节假日并计入调休周末的工作时段",
            configuredDue == new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.FromHours(8)));

        var invalid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApprovalService:Notifications:Calendar:WorkingStart"] = "18:00",
            ["ApprovalService:Notifications:Calendar:WorkingEnd"] = "09:00",
        }).Build();
        check("无效工作时段在配置装配时拒绝",
            ThrowsInvalidConfiguration(() => NotificationCalendarSettings.Create(invalid)));

        var directory = TempDataDirectory.Create();
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "deadlines.db")).Options;
            var factory = new DeadlineContextFactory(options);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Flows.Add(new FlowEntity
                {
                    Id = "flow-deadline",
                    Code = "DEADLINE",
                    Name = "时限审批",
                    PublishedVersionId = "version-deadline",
                });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "version-deadline",
                    FlowId = "flow-deadline",
                    Version = 1,
                    SchemaVersion = FlowSchema.CurrentVersion,
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                await db.SaveChangesAsync();
            }

            var calendarService = new WorkCalendarAppService(new WorkCalendarRepository(factory),
                new WorkCalendarDefaults(new WorkCalendarResponse
                {
                    TimeZoneId = "Asia/Shanghai",
                    WorkingStart = "09:00",
                    WorkingEnd = "18:00",
                }));
            var oldCalendar = await calendarService.GetAsync();
            var service = new ApprovalRuntimeAppService(new ApprovalRuntimeRepository(factory),
                calendarProvider: calendarService);
            var startContext = EngineScenarioChecks.Context(graph);
            startContext.AssigneeSnapshot = new AssigneeSnapshot
            {
                InitiatorId = "requester",
                People = [new() { Id = "requester" }, new() { Id = "user-n1" }, new() { Id = "user-next" }],
            };
            startContext.OccurredAt = new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);
            var started = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = EngineScenarioChecks.Command(EngineCommandKinds.Start, "deadline-start"),
                Context = startContext,
                StartInstance = new InstanceEntity
                {
                    Id = "instance",
                    SerialNo = "AP-DUE-1",
                    FlowVersionId = "version-deadline",
                    FlowCode = "DEADLINE",
                    BusinessKey = "B-DUE-1",
                    Title = "时限审批",
                    InitiatorId = "requester",
                    ClientAppCode = "demo",
                },
            });
            var firstId = started.State.Tasks.Single(task => task.Status == EngineTaskStatuses.Pending).Id;
            var queuedId = started.State.Tasks.Single(task => task.Status == EngineTaskStatuses.Queued).Id;
            var firstDue = new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.FromHours(8));
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("新待办按实际开始时间将工作分钟期限与审批状态原子写入 SQLite",
                    await db.ApprovalTasks.Where(task => task.Id == firstId)
                        .Select(task => task.DueAt).SingleAsync() == firstDue);
                check("排队任务尚未开始计时", (await db.ApprovalTasks.SingleAsync(task => task.Id == queuedId)).DueAt is null);
                var reminders = await db.OutboxItems.AsNoTracking()
                    .Where(item => item.Kind == DeadlineReminderPlanner.OutboxKind).ToArrayAsync();
                check("新待办的提前一个工作小时与超时提醒随审批事务固定排期",
                    reminders.Length == 2 && reminders.All(item =>
                    {
                        var payload = JsonSerializer.Deserialize<DeadlineReminderPayload>(item.PayloadJson);
                        return payload is not null && payload.TaskId == firstId && payload.DueAt == firstDue &&
                            payload.ScheduledAt == item.AvailableAt;
                    }));
            }

            await calendarService.SaveAsync(new UpdateWorkCalendarRequest
            {
                ExpectedRevision = 0,
                WorkingStart = "10:00",
                WorkingEnd = "12:00",
                Reason = "调整节假日安排",
                Overrides = [new() { Date = "2026-10-16", IsWorkingDay = false },
                    new() { Date = "2026-10-17", IsWorkingDay = true }],
            }, "calendar-admin");
            var newCalendar = await calendarService.GetAsync();
            var probe = new DateTimeOffset(2026, 10, 16, 7, 0, 0, TimeSpan.Zero);
            check("在线保存不修改已取得的日历快照，新读取同时采用新时段和逐日覆盖",
                oldCalendar.Calculate(graph, firstTask, probe) == probe.AddHours(1) &&
                newCalendar.Calculate(graph, firstTask, probe) ==
                    new DateTimeOffset(2026, 10, 17, 11, 0, 0, TimeSpan.FromHours(8)));
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("在线修改日历不重算已有待办期限和已经排定的提醒",
                    (await db.ApprovalTasks.SingleAsync(task => task.Id == firstId)).DueAt == firstDue &&
                    (await db.OutboxItems.Where(item => item.Kind == DeadlineReminderPlanner.OutboxKind)
                        .ToArrayAsync()).All(item =>
                        JsonSerializer.Deserialize<DeadlineReminderPayload>(item.PayloadJson)!.DueAt == firstDue));
            }
            var nextContext = EngineScenarioChecks.Context(graph);
            nextContext.OccurredAt = new DateTimeOffset(2026, 10, 16, 7, 0, 0, TimeSpan.Zero);
            var advanced = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = EngineScenarioChecks.Command(EngineCommandKinds.Approve, "deadline-approve", firstId),
                Context = nextContext,
            });
            var secondId = advanced.State.Tasks.Single(task => task.Status == EngineTaskStatuses.Pending).Id;
            await using (var db = await factory.CreateDbContextAsync())
            {
                var projections = await db.ApprovalTasks.AsNoTracking()
                    .Where(task => task.InstanceId == "instance").ToDictionaryAsync(task => task.Id);
                check("前一任务期限不重算，同一服务的下一命令按新日历计算排队激活任务",
                    secondId == queuedId && projections[firstId].DueAt == firstDue &&
                    projections[secondId].DueAt == new DateTimeOffset(2026, 10, 17, 11, 0, 0, TimeSpan.FromHours(8)));
                var newReminders = (await db.OutboxItems.Where(item => item.Kind == DeadlineReminderPlanner.OutboxKind)
                    .ToArrayAsync()).Select(item => JsonSerializer.Deserialize<DeadlineReminderPayload>(item.PayloadJson)!)
                    .Where(item => item.TaskId == secondId).ToArray();
                check("新任务的提前提醒和期限使用同一新日历快照",
                    newReminders.Single(item => item.ReminderKind == DeadlineReminderPlanner.BeforeDue).ScheduledAt ==
                        new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.FromHours(8)));
                check("下一任务独立产生两条提醒而不重复安排旧任务",
                    await db.OutboxItems.CountAsync(item => item.Kind == DeadlineReminderPlanner.OutboxKind) == 4);
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 判断配置装配是否因不合法的工作时段失败。
    /// </summary>
    /// <param name="action">待执行配置装配。</param>
    /// <returns>以配置错误拒绝为真。</returns>
    private static bool ThrowsInvalidConfiguration(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>
    /// 为每次操作建立独立的 SQLite 上下文。
    /// </summary>
    /// <param name="options">共享测试库选项。</param>
    private sealed class DeadlineContextFactory(DbContextOptions<ApprovalDbContext> options)
        : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
