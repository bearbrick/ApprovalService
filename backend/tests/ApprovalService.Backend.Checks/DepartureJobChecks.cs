using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 验证组织离职事务内排程与后台任务的重启、重试。
/// </summary>
internal static class DepartureJobChecks
{
    /// <summary>
    /// 运行可信组织推送到引擎改派的完整场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-departure-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "jobs.db") + ";Default Timeout=10").Options;
            var factory = new ContextFactory(options);
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            graph.Nodes[1].Approval = new FlowApprovalSettings
            {
                InactiveAssigneeBehavior = FlowInactiveAssigneeBehaviors.Manager,
            };
            await SeedAsync(factory, graph);
            var repository = new ApprovalRuntimeRepository(factory);
            var runtime = new ApprovalRuntimeAppService(repository);
            var organization = new OrganizationAppService(new OrganizationRepository(factory));
            await StartAsync(runtime, graph, "first", "departed");
            await StartAsync(runtime, graph, "second", "departed");
            await StartAsync(runtime, graph, "blocked", "no-manager");
            var occurredAt = new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.Zero);
            var departed = await organization.PushChangesAsync(new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "departed", EmployeeNumber = "E1", Name = "离职人员",
                    ManagerId = "manager", IsActive = false }],
            }, "demo", occurredAt);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("可信离职推送同事务写提醒与持久改派任务",
                    departed.DepartedPersonIds.SequenceEqual(["departed"]) &&
                    await db.ScheduledJobs.CountAsync(item => item.Kind == DepartureJobHandler.JobKind) == 1 &&
                    await db.DepartureNotices.AnyAsync(item => item.PersonId == "departed" &&
                        item.PendingTaskCount == 2));
            }

            // 在推送后新建依赖容器和后台执行器，模拟服务退出再启动。
            using var services = Services(factory);
            var worker = Worker(factory, services);
            await worker.RunOnceAsync(occurredAt.AddSeconds(1));
            await using (var db = await factory.CreateDbContextAsync())
            {
                var tasks = await db.ApprovalTasks.Where(item => item.InstanceId == "first" ||
                    item.InstanceId == "second").ToArrayAsync();
                check("服务重启后后台任务逐单走引擎改派并完成持久任务",
                    tasks.Length == 2 && tasks.All(item => item.AssigneeId == "manager") &&
                    await db.ScheduledJobs.AnyAsync(item => item.Kind == DepartureJobHandler.JobKind &&
                        item.Status == JobStatuses.Completed) &&
                    await db.DepartureNotices.AnyAsync(item => item.PersonId == "departed" &&
                        item.PendingTaskCount == 0));
            }
            check("多单离职改派后各自事件链仍完整",
                await runtime.VerifyEventChainAsync("first") && await runtime.VerifyEventChainAsync("second"));

            var blockedAt = occurredAt.AddMinutes(3);
            await organization.PushChangesAsync(new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "no-manager", EmployeeNumber = "E2", Name = "待补上级",
                    IsActive = false }],
            }, "demo", blockedAt);
            await worker.RunOnceAsync(blockedAt.AddSeconds(1));
            DateTimeOffset retryAt;
            await using (var db = await factory.CreateDbContextAsync())
            {
                var retry = await db.ScheduledJobs.SingleAsync(item => item.Kind == DepartureJobHandler.JobKind &&
                    item.Status == JobStatuses.Pending);
                retryAt = retry.DueAt;
                check("无在职接手人时保留任务并持久退避重试",
                    retry.Attempts == 1 && retry.DueAt > blockedAt &&
                    retry.LastError == ErrorCodes.ValidationFailed &&
                    await db.ApprovalTasks.AnyAsync(item => item.InstanceId == "blocked" &&
                        item.AssigneeId == "no-manager") &&
                    await db.DepartureNotices.AnyAsync(item => item.PersonId == "no-manager" &&
                        item.PendingTaskCount == 1));
            }
            await organization.PushChangesAsync(new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "no-manager", EmployeeNumber = "E2", Name = "待补上级",
                    ManagerId = "manager", IsActive = false }],
            }, "demo", retryAt);
            await worker.RunOnceAsync(retryAt.AddSeconds(1));
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("补齐组织上级后同一持久任务重试成功且只留一条改派事件",
                    await db.ApprovalTasks.AnyAsync(item => item.InstanceId == "blocked" &&
                        item.AssigneeId == "manager") &&
                    await db.OperationEvents.CountAsync(item => item.InstanceId == "blocked" &&
                        item.Kind == "inactiveTaskReassigned") == 1 &&
                    await db.ScheduledJobs.CountAsync(item => item.Kind == DepartureJobHandler.JobKind &&
                        item.Status == JobStatuses.Completed && item.LastError == null) == 2);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 初始化发布流程、接入应用和可信组织人员。
    /// </summary>
    /// <param name="factory">数据库上下文工厂。</param>
    /// <param name="graph">发布图。</param>
    /// <returns>异步操作。</returns>
    private static async Task SeedAsync(ContextFactory factory, FlowExecutionGraph graph)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.ClientApps.Add(new ClientAppEntity
        {
            Code = "demo",
            Name = "示例系统",
            ApiKeyHash = new string('a', 64),
            SigningSecretCiphertext = "test-ciphertext",
        });
        db.Flows.Add(new FlowEntity
        {
            Id = "flow",
            Code = "DEPARTURE",
            Name = "离职审批",
            AdministratorId = "administrator",
            PublishedVersionId = "version",
        });
        db.FlowVersions.Add(new FlowVersionEntity
        {
            Id = "version",
            FlowId = "flow",
            Version = 1,
            SchemaVersion = FlowSchema.CurrentVersion,
            DefinitionJson = "{}",
            GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
        });
        db.People.AddRange(
            new PersonEntity
            {
                Id = "departed",
                EmployeeNumber = "E1",
                Name = "离职人员",
                ManagerId = "manager",
                TrustedSource = "demo"
            },
            new PersonEntity
            {
                Id = "no-manager",
                EmployeeNumber = "E2",
                Name = "待补上级",
                TrustedSource = "demo"
            },
            new PersonEntity
            {
                Id = "manager",
                EmployeeNumber = "E3",
                Name = "在职上级",
                TrustedSource = "approvalService"
            },
            new PersonEntity
            {
                Id = "administrator",
                EmployeeNumber = "E4",
                Name = "流程管理员",
                TrustedSource = "approvalService"
            });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 发起指定人员的在途审批。
    /// </summary>
    /// <param name="runtime">运行时服务。</param>
    /// <param name="graph">发布图。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="assigneeId">审批人 ID。</param>
    /// <returns>异步操作。</returns>
    private static async Task StartAsync(ApprovalRuntimeAppService runtime, FlowExecutionGraph graph,
        string instanceId, string assigneeId)
    {
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = EngineCommandKinds.Start,
                InstanceId = instanceId,
                RequestId = "start-" + instanceId
            },
            Context = new EngineContext
            {
                Graph = graph,
                OccurredAt = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
                Assignees = new Dictionary<string, string> { ["n1"] = assigneeId },
            },
            StartInstance = new InstanceEntity
            {
                Id = instanceId,
                SerialNo = "AP-" + instanceId,
                FlowVersionId = "version",
                FlowCode = "DEPARTURE",
                BusinessKey = "BUSINESS-" + instanceId,
                Title = "离职场景",
                InitiatorId = "manager",
                ClientAppCode = "demo",
            },
        });
    }

    /// <summary>
    /// 在重启后的容器中登记真实仓储和后台离职处理器。
    /// </summary>
    /// <param name="factory">数据库上下文工厂。</param>
    /// <returns>依赖提供器。</returns>
    private static ServiceProvider Services(ContextFactory factory) => new ServiceCollection()
        .AddSingleton<IDbContextFactory<ApprovalDbContext>>(factory)
        .AddScoped<IApprovalRuntimeRepository, ApprovalRuntimeRepository>()
        .AddScoped<IApprovalRuntimeAppService, ApprovalRuntimeAppService>()
        .AddScoped<IOrganizationRepository, OrganizationRepository>()
        .AddScoped<IOrganizationLookupAppService, OrganizationLookupAppService>()
        .AddScoped<IScheduledJobHandler, DepartureJobHandler>()
        .AddScoped<IOutboxHandler, SinkOutboxHandler>()
        .BuildServiceProvider();

    /// <summary>
    /// 使用持久队列创建后台执行器。
    /// </summary>
    /// <param name="factory">数据库上下文工厂。</param>
    /// <param name="services">重启后的容器。</param>
    /// <returns>后台执行器。</returns>
    private static DurableJobWorker Worker(ContextFactory factory, ServiceProvider services) => new(
        new OutboxQueue(factory), new ScheduledJobQueue(factory),
        services.GetRequiredService<IServiceScopeFactory>());

    /// <summary>
    /// 每次创建独立的 SQLite 数据库上下文。
    /// </summary>
    /// <param name="options">上下文选项。</param>
    private sealed class ContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 检查时把引擎事件投递视为成功，避免外部回调干扰离职任务消费。
    /// </summary>
    private sealed class SinkOutboxHandler : IOutboxHandler
    {
        /// <inheritdoc />
        public string Kind => "engineEvent";

        /// <inheritdoc />
        public Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
