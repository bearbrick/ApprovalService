using System.Text.Json;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 检查在途业务单号占用与结束后再次发起。
/// </summary>
internal static class OpenApiChecksActiveKeys
{
    /// <summary>
    /// 覆盖重复在途发起、结束释放和保留历史审批单。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-active-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "active.db")).Options;
            var factory = new ActiveContextFactory(options);
            var graph = EngineScenarioChecks.Chain(ApprovalService.Contracts.Flows.FlowNodeTypes.Approval);
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
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                await db.SaveChangesAsync();
            }

            var service = new ApprovalRuntimeAppService(new ApprovalRuntimeRepository(factory));
            var first = await service.ExecuteAsync(Start("first", "one", graph));
            var duplicate = false;
            try
            {
                await service.ExecuteAsync(Start("second", "two", graph));
            }
            catch (UserFriendlyException error) when (error.Code == "CONFLICT")
            {
                duplicate = true;
            }

            check("在途审批单阻止同流程同业务单号新建第二张", duplicate);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("在途唯一键与审批单状态同事务保存", await db.ActiveInstanceKeys.CountAsync() == 1 &&
                    await db.Instances.CountAsync() == 1);
            }

            await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = new EngineCommand
                {
                    Kind = EngineCommandKinds.Approve,
                    InstanceId = "first",
                    RequestId = "approve",
                    TaskId = first.State.Tasks.Single().Id,
                },
                Context = EngineScenarioChecks.Context(graph),
            });
            var newApproval = await service.ExecuteAsync(Start("third", "three", graph));
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("审批结束释放占用且同业务单号可再发起并保留历史", newApproval.State.Status == "running" &&
                    await db.Instances.CountAsync() == 2 && await db.ActiveInstanceKeys.CountAsync() == 1 &&
                    await db.ActiveInstanceKeys.SingleAsync() is { InstanceId: "third" });
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 构造相同业务单号的独立发起命令。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="graph">发布执行图。</param>
    /// <returns>可由运行应用服务执行的输入。</returns>
    private static RuntimeCommandInput Start(string instanceId, string requestId,
        ApprovalService.Contracts.Flows.FlowExecutionGraph graph) => new()
        {
            Command = new EngineCommand { Kind = EngineCommandKinds.Start, InstanceId = instanceId, RequestId = requestId },
            Context = EngineScenarioChecks.Context(graph),
            StartInstance = new InstanceEntity
            {
                Id = instanceId,
                SerialNo = "AP-" + instanceId,
                FlowVersionId = "version",
                FlowCode = "PURCHASE",
                BusinessKey = "PO-1",
                Title = "采购",
                InitiatorId = "requester",
                ClientAppCode = "demo",
            },
        };

    /// <summary>
    /// 每次创建短生命周期 SQLite 上下文。
    /// </summary>
    /// <param name="options">数据库配置。</param>
    private sealed class ActiveContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
