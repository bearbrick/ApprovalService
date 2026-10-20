using System.Text.Json;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 核对三种数据库的迁移与当前模型，并可选在真实外部数据库跑主要命令链。
/// </summary>
internal static class DatabaseProviderChecks
{
    /// <summary>
    /// 运行迁移一致性和可选外部数据库场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var sqliteOptions = new DbContextOptionsBuilder<SqliteApprovalDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        var sqlServerOptions = new DbContextOptionsBuilder<SqlServerApprovalDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ApprovalServiceModelCheck;Trusted_Connection=True")
            .Options;
        var postgresOptions = new DbContextOptionsBuilder<PostgreSqlApprovalDbContext>()
            .UseNpgsql("Host=localhost;Database=ApprovalServiceModelCheck;Username=check;Password=check-only")
            .Options;
        await using var sqlite = new SqliteApprovalDbContext(sqliteOptions);
        await using var sqlServer = new SqlServerApprovalDbContext(sqlServerOptions);
        await using var postgres = new PostgreSqlApprovalDbContext(postgresOptions);
        check("SQLite、SQL Server、PostgreSQL 均有独立的初始迁移",
            sqlite.Database.GetMigrations().Any() &&
            sqlServer.Database.GetMigrations().Any() &&
            postgres.Database.GetMigrations().Any());
        check("三套迁移快照都与当前统一模型一致",
            !sqlite.Database.HasPendingModelChanges() &&
            !sqlServer.Database.HasPendingModelChanges() &&
            !postgres.Database.HasPendingModelChanges());

        var sqlServerConnection = Environment.GetEnvironmentVariable("APPROVAL_TEST_SQLSERVER");
        if (!string.IsNullOrWhiteSpace(sqlServerConnection))
        {
            var options = new DbContextOptionsBuilder<SqlServerApprovalDbContext>()
                .UseSqlServer(sqlServerConnection).Options;
            await RunRealDatabaseAsync(() => new SqlServerApprovalDbContext(options), "SQL Server", check);
        }

        var postgresConnection = Environment.GetEnvironmentVariable("APPROVAL_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(postgresConnection))
        {
            var options = new DbContextOptionsBuilder<PostgreSqlApprovalDbContext>()
                .UseNpgsql(postgresConnection).Options;
            await RunRealDatabaseAsync(() => new PostgreSqlApprovalDbContext(options), "PostgreSQL", check);
        }
    }

    /// <summary>
    /// 在明确指定的测试数据库创建流程并执行发起与同意，随后删除这次生成的数据。
    /// </summary>
    /// <param name="createContext">创建该提供程序上下文的函数。</param>
    /// <param name="providerName">检查日志中的提供程序名称。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunRealDatabaseAsync(Func<ApprovalDbContext> createContext,
        string providerName, Action<string, bool> check)
    {
        var key = Guid.NewGuid().ToString("N");
        var flowId = "check-flow-" + key;
        var versionId = "check-version-" + key;
        var instanceId = "check-instance-" + key;
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        var seeded = false;
        try
        {
            await using (var db = createContext())
            {
                await db.Database.MigrateAsync();
                db.Flows.Add(new FlowEntity
                {
                    Id = flowId,
                    Code = "CHECK-" + key,
                    Name = "测试流程",
                    PublishedVersionId = versionId,
                });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = versionId,
                    FlowId = flowId,
                    Version = 1,
                    SchemaVersion = FlowSchema.CurrentVersion,
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                await db.SaveChangesAsync();
                seeded = true;
            }

            var repository = new ApprovalRuntimeRepository(new DelegateContextFactory(createContext));
            var service = new ApprovalRuntimeAppService(repository);
            var start = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = new EngineCommand
                {
                    Kind = EngineCommandKinds.Start,
                    RequestId = "start-" + key,
                    InstanceId = instanceId,
                },
                Context = EngineScenarioChecks.Context(graph),
                StartInstance = new InstanceEntity
                {
                    Id = instanceId,
                    SerialNo = "CHECK-" + key,
                    FlowVersionId = versionId,
                    FlowCode = "CHECK-" + key,
                    BusinessKey = key,
                    Title = "数据库场景",
                    InitiatorId = "checker",
                    ClientAppCode = "checks",
                },
            });
            var done = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = new EngineCommand
                {
                    Kind = EngineCommandKinds.Approve,
                    RequestId = "approve-" + key,
                    InstanceId = instanceId,
                    TaskId = start.State.Tasks.Single().Id,
                },
                Context = EngineScenarioChecks.Context(graph),
            });
            check(providerName + " 真实数据库可发起、审批并校验操作链",
                done.State.Status == EngineInstanceStatuses.Approved &&
                await service.VerifyEventChainAsync(instanceId));
        }
        finally
        {
            if (seeded)
            {
                await using var db = createContext();
                await db.OutboxItems.Where(item => item.InstanceId == instanceId).ExecuteDeleteAsync();
                await db.OperationEvents.Where(item => item.InstanceId == instanceId).ExecuteDeleteAsync();
                await db.ApprovalTasks.Where(item => item.InstanceId == instanceId).ExecuteDeleteAsync();
                await db.Instances.Where(item => item.Id == instanceId).ExecuteDeleteAsync();
                await db.FlowVersions.Where(item => item.Id == versionId).ExecuteDeleteAsync();
                await db.Flows.Where(item => item.Id == flowId).ExecuteDeleteAsync();
            }
        }
    }

    /// <summary>
    /// 为仓储的每次事务创建独立上下文。
    /// </summary>
    /// <param name="createContext">创建指定提供程序上下文的函数。</param>
    private sealed class DelegateContextFactory(Func<ApprovalDbContext> createContext) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => createContext();

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
