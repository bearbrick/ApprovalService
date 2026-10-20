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
/// 检查事件链验证在并发提交边界只使用一致的锚点与记录快照。
/// </summary>
internal static class EventChainConsistencyChecks
{
    /// <summary>
    /// 在旧式两次读取之间插入真实审批提交，检查验证不产生短暂误报。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-chain-consistency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "consistency.db"))
                .Options;
            var factory = new ConsistencyContextFactory(options);
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Flows.Add(new FlowEntity
                {
                    Id = "consistency-flow",
                    Code = "CONSISTENCY",
                    Name = "事件链一致读取",
                    PublishedVersionId = "consistency-version",
                });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "consistency-version",
                    FlowId = "consistency-flow",
                    Version = 1,
                    SchemaVersion = FlowSchema.CurrentVersion,
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                await db.SaveChangesAsync();
            }

            var repository = new ApprovalRuntimeRepository(factory);
            var service = new ApprovalRuntimeAppService(repository);
            var started = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = EngineScenarioChecks.Command(EngineCommandKinds.Start, "consistency-start"),
                Context = EngineScenarioChecks.Context(graph),
                StartInstance = new InstanceEntity
                {
                    Id = "instance",
                    SerialNo = "AP-CONSISTENCY",
                    FlowVersionId = "consistency-version",
                    FlowCode = "CONSISTENCY",
                    BusinessKey = "CONSISTENCY-001",
                    Title = "事件链并发检查",
                    InitiatorId = "requester",
                    ClientAppCode = "demo",
                },
            });

            var appendCount = 0;
            Func<Task> appendApproval = async () =>
            {
                appendCount++;
                await service.ExecuteAsync(new RuntimeCommandInput
                {
                    Command = EngineScenarioChecks.Command(EngineCommandKinds.Approve,
                        "consistency-approve", started.State.Tasks.Single().Id),
                    Context = EngineScenarioChecks.Context(graph),
                });
            };

            var racingRepository = new InterleavingRepository(repository, appendApproval);
            var verifier = new ApprovalRuntimeAppService(racingRepository);
            check("并发提交后事件链验证从同一快照读取锚点和记录",
                await verifier.VerifyEventChainAsync("instance") && appendCount == 1 &&
                racingRepository.AtomicReadCount == 1 && racingRepository.SplitReadCount == 0);
            check("并发提交后的完整事件链仍可直接校验", await service.VerifyEventChainAsync("instance"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 为检查程序按需创建独立的 SQLite 上下文。
    /// </summary>
    /// <param name="options">临时库配置。</param>
    private sealed class ConsistencyContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 在验证读取边界安排一次真正的数据库提交，暴露分开读取时的旧锚点问题。
    /// </summary>
    /// <param name="inner">真实数据库仓储。</param>
    /// <param name="append">读取边界要提交的审批命令。</param>
    private sealed class InterleavingRepository(IApprovalRuntimeRepository inner, Func<Task> append) : IApprovalRuntimeRepository
    {
        /// <summary>
        /// 一致快照读取次数。
        /// </summary>
        public int AtomicReadCount
        {
            get; private set;
        }

        /// <summary>
        /// 旧式分开读取次数。
        /// </summary>
        public int SplitReadCount
        {
            get; private set;
        }

        /// <inheritdoc />
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
            inner.CanConnectAsync(cancellationToken);

        /// <inheritdoc />
        public async Task<RuntimeSnapshot?> LoadAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            SplitReadCount++;
            var snapshot = await inner.LoadAsync(instanceId, cancellationToken);
            await append();
            return snapshot;
        }

        /// <inheritdoc />
        public Task<FlowVersionEntity?> LoadVersionAsync(string versionId, CancellationToken cancellationToken = default) =>
            inner.LoadVersionAsync(versionId, cancellationToken);

        /// <inheritdoc />
        public Task<bool> CommitAsync(RuntimeCommit commit, CancellationToken cancellationToken = default) =>
            inner.CommitAsync(commit, cancellationToken);

        /// <inheritdoc />
        public async Task<EventChainSnapshot?> LoadEventChainAsync(string instanceId,
            CancellationToken cancellationToken = default)
        {
            AtomicReadCount++;
            await append();
            return await inner.LoadEventChainAsync(instanceId, cancellationToken);
        }
    }
}
