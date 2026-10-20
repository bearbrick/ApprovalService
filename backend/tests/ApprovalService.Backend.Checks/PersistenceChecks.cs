using System.Text.Json;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 事务检查审批状态、乐观锁、事件链及待发项。
/// </summary>
internal static class PersistenceChecks
{
    /// <summary>
    /// 运行审批命令的持久化场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "runtime.db") + ";Default Timeout=10")
                .Options;
            var factory = new TestContextFactory(options);
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            await using (var context = await factory.CreateDbContextAsync())
            {
                await context.Database.EnsureCreatedAsync();
                context.Flows.Add(new FlowEntity
                {
                    Id = "flow",
                    Code = "PURCHASE",
                    Name = "采购审批",
                    PublishedVersionId = "version",
                });
                context.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "version",
                    FlowId = "flow",
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
                Command = EngineScenarioChecks.Command(EngineCommandKinds.Start, "start"),
                Context = EngineScenarioChecks.Context(graph),
                StartInstance = new InstanceEntity
                {
                    Id = "instance",
                    SerialNo = "AP-0001",
                    FlowVersionId = "version",
                    FlowCode = "PURCHASE",
                    BusinessKey = "PO-001",
                    Title = "采购申请",
                    InitiatorId = "requester",
                    ClientAppCode = "demo",
                },
            });
            check("发起命令同时保存状态、待办、事件和待发项",
                start.State.Tasks.Count == 1 && await CountMatchesAsync(factory, 1, 1,
                    start.Events.Count, start.Events.Count));
            var replay = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = EngineScenarioChecks.Command(EngineCommandKinds.Start, "start"),
                Context = EngineScenarioChecks.Context(graph),
            });
            check("重复请求 ID 不重复写状态或事件", replay.IsReplay &&
                await CountMatchesAsync(factory, 1, 1, start.Events.Count, start.Events.Count));

            // 两个应用服务各自从独立上下文读取同一版本，闸门确保它们真正并发提交。
            var gate = new LoadGate();
            var firstService = new ApprovalRuntimeAppService(new GatedRepository(new ApprovalRuntimeRepository(factory), gate));
            var secondService = new ApprovalRuntimeAppService(new GatedRepository(new ApprovalRuntimeRepository(factory), gate));
            var taskId = start.State.Tasks.Single().Id;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var first = CaptureAsync(() => firstService.ExecuteAsync(
                ApprovalInput(EngineScenarioChecks.Command(EngineCommandKinds.Approve, "approve-one", taskId), graph),
                timeout.Token));
            var second = CaptureAsync(() => secondService.ExecuteAsync(
                ApprovalInput(EngineScenarioChecks.Command(EngineCommandKinds.Approve, "approve-two", taskId), graph),
                timeout.Token));
            var outcomes = await Task.WhenAll(first, second);
            check("两人并发提交时仅一条命令推进状态", outcomes.Count(error => error is null) == 1 &&
                outcomes.Count(error => error is UserFriendlyException { Code: ErrorCodes.Conflict }) == 1);
            await using (var context = await factory.CreateDbContextAsync())
            {
                var instance = await context.Instances.SingleAsync(item => item.Id == "instance");
                check("乐观锁版本只增加一次且审批单只结束一次", instance.Version == 2 &&
                    instance.Status == EngineInstanceStatuses.Approved &&
                    await context.OperationEvents.CountAsync(item => item.InstanceId == "instance" &&
                        item.Kind == "instanceApproved") == 1);
            }

            check("只追加操作记录的顺序和前条校验值完整", await service.VerifyEventChainAsync("instance"));
            await using (var context = await factory.CreateDbContextAsync())
            {
                // 绕过正常 SaveChanges 的只追加防护，模拟运维或外部工具直接篡改数据库。
                await context.OperationEvents.Where(item => item.InstanceId == "instance" && item.Sequence == 1)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Kind, "tampered"));
            }

            check("历史操作记录被修改后能查出校验链损坏",
                !await service.VerifyEventChainAsync("instance"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 建立审批命令输入。
    /// </summary>
    /// <param name="command">引擎命令。</param>
    /// <param name="graph">用来提供固定人员的测试执行图。</param>
    /// <returns>应用服务输入。</returns>
    private static RuntimeCommandInput ApprovalInput(EngineCommand command, FlowExecutionGraph graph) => new()
    {
        Command = command,
        Context = EngineScenarioChecks.Context(graph),
    };

    /// <summary>
    /// 读取各表行数，确认事务内的投影和事件一起写入。
    /// </summary>
    /// <param name="factory">独立上下文工厂。</param>
    /// <param name="instances">预期审批单数。</param>
    /// <param name="tasks">预期任务数。</param>
    /// <param name="events">预期事件数。</param>
    /// <param name="outbox">预期待发项数。</param>
    /// <returns>四张表行数均符合预期为真。</returns>
    private static async Task<bool> CountMatchesAsync(TestContextFactory factory, int instances, int tasks,
        int events, int outbox)
    {
        await using var context = await factory.CreateDbContextAsync();
        return await context.Instances.CountAsync() == instances &&
            await context.ApprovalTasks.CountAsync() == tasks &&
            await context.OperationEvents.CountAsync() == events &&
            await context.OutboxItems.CountAsync() == outbox;
    }

    /// <summary>
    /// 将并发任务的成功或用户可纠正的冲突转换成可断言的结果。
    /// </summary>
    /// <param name="action">要执行的异步命令。</param>
    /// <returns>成功时为空，否则为原始异常。</returns>
    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    /// <summary>
    /// 创建新的上下文，让并发命令共享同一个数据库文件但不共享跟踪器。
    /// </summary>
    /// <param name="options">指向同一个临时 SQLite 文件的配置。</param>
    private sealed class TestContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 保证两个并发审批都读取相同旧版本后才允许提交。
    /// </summary>
    private sealed class LoadGate
    {
        /// <summary>
        /// 已到达读取闸门的调用数。
        /// </summary>
        private int arrivals;

        /// <summary>
        /// 两个调用到齐时释放的信号。
        /// </summary>
        private readonly TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 到达闸门并等待另一调用。
        /// </summary>
        /// <param name="cancellationToken">取消标记。</param>
        /// <returns>异步操作。</returns>
        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                released.TrySetResult(true);
            }

            await released.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 仅在读取审批单后加入并发闸门，其余操作委托真实数据库仓储。
    /// </summary>
    /// <param name="inner">实际访问 SQLite 的仓储。</param>
    /// <param name="gate">两次读取共用的并发闸门。</param>
    private sealed class GatedRepository(IApprovalRuntimeRepository inner, LoadGate gate) : IApprovalRuntimeRepository
    {
        /// <inheritdoc />
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
            inner.CanConnectAsync(cancellationToken);

        /// <inheritdoc />
        public async Task<RuntimeSnapshot?> LoadAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            var snapshot = await inner.LoadAsync(instanceId, cancellationToken);
            await gate.ArriveAsync(cancellationToken);
            return snapshot;
        }

        /// <inheritdoc />
        public Task<FlowVersionEntity?> LoadVersionAsync(string versionId, CancellationToken cancellationToken = default) =>
            inner.LoadVersionAsync(versionId, cancellationToken);

        /// <inheritdoc />
        public Task<bool> CommitAsync(RuntimeCommit commit, CancellationToken cancellationToken = default) =>
            inner.CommitAsync(commit, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<OperationEventEntity>> LoadEventsAsync(string instanceId,
            CancellationToken cancellationToken = default) => inner.LoadEventsAsync(instanceId, cancellationToken);
    }
}
