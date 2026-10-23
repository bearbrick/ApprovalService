using System.Text.Json;
using ApprovalService.Application.Notifications;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
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

            var service = new ApprovalRuntimeAppService(new ApprovalRuntimeRepository(factory));
            var startContext = EngineScenarioChecks.Context(graph);
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
            var firstId = started.State.Tasks.Single().Id;
            var firstDue = new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.FromHours(8));
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("新待办按实际开始时间将工作分钟期限与审批状态原子写入 SQLite",
                    await db.ApprovalTasks.Where(task => task.Id == firstId)
                        .Select(task => task.DueAt).SingleAsync() == firstDue);
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
                check("前一任务的期限不随下一节点推进重算，下一待办从实际激活时刻起算",
                    projections[firstId].DueAt == firstDue &&
                    projections[secondId].DueAt == new DateTimeOffset(2026, 10, 16, 16, 0, 0, TimeSpan.FromHours(8)));
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
