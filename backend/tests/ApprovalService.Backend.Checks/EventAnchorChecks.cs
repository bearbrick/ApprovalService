using System.Text.Json;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 检查操作链尾锚点与事件删除检测。
/// </summary>
internal static class EventAnchorChecks
{
    /// <summary>
    /// 发起审批后直接删除尾部事件，检查锚点仍能发现缺失。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-anchor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "anchor.db"))
                .Options;
            var factory = new AnchorContextFactory(options);
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            await using (var context = await factory.CreateDbContextAsync())
            {
                await context.Database.EnsureCreatedAsync();
                context.Flows.Add(new FlowEntity
                {
                    Id = "anchor-flow",
                    Code = "ANCHOR",
                    Name = "锚点检查",
                    PublishedVersionId = "anchor-version",
                });
                context.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "anchor-version",
                    FlowId = "anchor-flow",
                    Version = 1,
                    SchemaVersion = FlowSchema.CurrentVersion,
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                await context.SaveChangesAsync();
            }

            var repository = new ApprovalRuntimeRepository(factory);
            var service = new ApprovalRuntimeAppService(repository);
            var start = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = EngineScenarioChecks.Command(EngineCommandKinds.Start, "anchor-start"),
                Context = EngineScenarioChecks.Context(graph),
                StartInstance = new InstanceEntity
                {
                    Id = "instance",
                    SerialNo = "AP-ANCHOR",
                    FlowVersionId = "anchor-version",
                    FlowCode = "ANCHOR",
                    BusinessKey = "ANCHOR-001",
                    Title = "尾部删除检查",
                    InitiatorId = "requester",
                    ClientAppCode = "demo",
                },
            });
            await using (var context = await factory.CreateDbContextAsync())
            {
                var instance = await context.Instances.SingleAsync(item => item.Id == "instance");
                var tail = await context.OperationEvents.Where(item => item.InstanceId == "instance")
                    .OrderByDescending(item => item.Sequence).FirstAsync();
                check("最终事件序号和校验值与审批状态同事务保存",
                    start.Events.Count > 0 && instance.LastEventSequence == tail.Sequence &&
                    instance.LastEventHash == tail.Hash && await service.VerifyEventChainAsync("instance"));

                // 绕过正常 SaveChanges 的只追加防护，模拟数据库管理工具误删最后一条记录。
                await context.OperationEvents.Where(item => item.Id == tail.Id).ExecuteDeleteAsync();
            }

            check("尾部操作记录被删除后锚点校验失败", !await service.VerifyEventChainAsync("instance"));
            var appendRejected = false;
            try
            {
                await service.ExecuteAsync(new RuntimeCommandInput
                {
                    Command = EngineScenarioChecks.Command(EngineCommandKinds.Approve,
                        "anchor-approve", start.State.Tasks.Single().Id),
                    Context = EngineScenarioChecks.Context(graph),
                });
            }
            catch (InvalidOperationException)
            {
                appendRejected = true;
            }

            check("尾部记录丢失后不允许继续追加操作", appendRejected);
            await using (var context = await factory.CreateDbContextAsync())
            {
                var instance = await context.Instances.SingleAsync(item => item.Id == "instance");
                var entries = await context.OperationEvents.Where(item => item.InstanceId == "instance")
                    .OrderBy(item => item.Sequence).ToListAsync();
                check("删除事件不改变审批单锚点", instance.LastEventSequence == start.Events.Count &&
                    !OperationHash.Verify(entries, instance.LastEventSequence, instance.LastEventHash));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 每次操作提供独立的数据库上下文。
    /// </summary>
    /// <param name="options">SQLite 临时文件配置。</param>
    private sealed class AnchorContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
