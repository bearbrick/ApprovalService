using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 以两个接入应用和同一审批人验证开放查询的应用范围与摘要字段。
/// </summary>
internal static class OpenApiChecksQueries
{
    /// <summary>
    /// 检查审批单授权、按业务单号查询、待办范围和数量。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-open-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "query.db")).Options;
            var factory = new QueryContextFactory(options);
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Flows.Add(new FlowEntity { Id = "flow", Code = "PURCHASE", Name = "采购", PublishedVersionId = "version" });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "version",
                    FlowId = "flow",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(new FlowDefinition
                    {
                        Data = new FlowDataDefinition { SummaryFields = ["amount"] },
                    }, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                await db.SaveChangesAsync();
            }

            var runtime = new ApprovalRuntimeAppService(new ApprovalRuntimeRepository(factory));
            await runtime.ExecuteAsync(Start("own", "demo", "PO-1", graph));
            await runtime.ExecuteAsync(Start("other", "another", "PO-2", graph));
            var queries = new OpenApprovalQueryAppService(new ApprovalOpenQueryRepository(factory), TimeProvider.System);
            var app = new ClientAppEntity { Code = "demo", CanViewAllTasks = false };
            var detail = await queries.GetInstanceAsync(app, "own");
            check("本应用可读取审批状态、待办和审计事件", detail.Status == "running" &&
                detail.Tasks.Count == 1 && detail.Events.Count > 0 && detail.Data.GetProperty("amount").GetInt32() == 100);
            var history = await queries.FindInstancesAsync(app, "PURCHASE", "PO-1");
            check("可按流程编码与业务单号查本应用审批单", history.Count == 1 && history[0].Id == "own");

            var forbidden = false;
            try
            {
                await queries.GetInstanceAsync(app, "other");
            }
            catch (UserFriendlyException error) when (error.Code == "FORBIDDEN")
            {
                forbidden = true;
            }

            check("接入应用不能读取其他业务系统的审批数据", forbidden);
            var ownCount = await queries.CountTasksAsync(app, "user-n1", allApps: false);
            var ownTasks = await queries.ListTasksAsync(app, "user-n1", allApps: false, skip: 0, take: 10);
            check("待办数量与列表默认只统计本接入应用", ownCount.Pending == 1 && ownTasks.Count == 1 &&
                ownTasks[0].InstanceId == "own" && ownTasks[0].Summary.GetProperty("amount").GetInt32() == 100 &&
                ownTasks[0].OpenPath.Contains("own", StringComparison.Ordinal));

            var allDenied = false;
            try
            {
                await queries.CountTasksAsync(app, "user-n1", allApps: true);
            }
            catch (UserFriendlyException error) when (error.Code == "FORBIDDEN")
            {
                allDenied = true;
            }

            check("未获统一待办授权的应用无法扩大统计范围", allDenied);
            app.CanViewAllTasks = true;
            var allCount = await queries.CountTasksAsync(app, "user-n1", allApps: true);
            var allTasks = await queries.ListTasksAsync(app, "user-n1", allApps: true, skip: 0, take: 10);
            check("获授权应用可以统计并列出这个人的全部待办", allCount.Pending == 2 && allTasks.Count == 2);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 构造带审批数据快照的发起命令。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="appCode">来源接入应用。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="graph">执行图。</param>
    /// <returns>运行命令。</returns>
    private static RuntimeCommandInput Start(string instanceId, string appCode, string businessKey,
        FlowExecutionGraph graph) => new()
        {
            Command = new EngineCommand { Kind = EngineCommandKinds.Start, RequestId = instanceId, InstanceId = instanceId },
            Context = EngineScenarioChecks.Context(graph),
            StartInstance = new InstanceEntity
            {
                Id = instanceId,
                SerialNo = "AP-" + instanceId,
                FlowVersionId = "version",
                FlowCode = "PURCHASE",
                BusinessKey = businessKey,
                Title = "采购",
                InitiatorId = "requester",
                ClientAppCode = appCode,
                ApprovalDataJson = "{\"amount\":100}",
            },
        };

    /// <summary>
    /// 临时数据库上下文工厂。
    /// </summary>
    /// <param name="options">SQLite 配置。</param>
    private sealed class QueryContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
