using System.Text.Json;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 检查离职候选、原子留痕及与审批的并发竞争。
/// </summary>
internal static class DeparturePersistenceChecks
{
    /// <summary>
    /// 执行持久化离职场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-departure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "departure.db") + ";Default Timeout=10").Options;
            var factory = new ContextFactory(options);
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            graph.Nodes[1].Approval = new FlowApprovalSettings
            {
                InactiveAssigneeBehavior = FlowInactiveAssigneeBehaviors.Manager,
            };
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Flows.Add(new FlowEntity
                {
                    Id = "flow",
                    Code = "DEPARTURE",
                    Name = "离职测试",
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
                await db.SaveChangesAsync();
            }

            var repository = new ApprovalRuntimeRepository(factory);
            var service = new ApprovalRuntimeAppService(repository);
            var firstTask = await StartAsync(service, graph, "first");
            var secondTask = await StartAsync(service, graph, "second");
            var candidates = await service.ListDepartureTasksAsync("user-n1");
            check("离职扫描按索引找到不同审批单的在途待办", candidates.Count == 2 &&
                candidates.Select(item => item.InstanceId).SequenceEqual(["first", "second"]));
            var firstRef = candidates.Single(item => item.TaskId == firstTask);
            var snapshot = Snapshot();
            var occurredAt = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
            var reassigned = await service.ReassignDepartedTaskAsync(firstRef, "user-n1", snapshot,
                occurredAt, "departure-first");
            var replay = await service.ReassignDepartedTaskAsync(firstRef, "user-n1", snapshot,
                occurredAt, "departure-first");
            check("离职命令提交后重试只返回幂等重放", reassigned.State.Tasks.Single().AssigneeId == "manager" &&
                replay.IsReplay && (await service.ListDepartureTasksAsync("user-n1")).Count == 1);
            await using (var db = await factory.CreateDbContextAsync())
            {
                var task = await db.ApprovalTasks.SingleAsync(item => item.Id == firstTask);
                var eventCount = await db.OperationEvents.CountAsync(item => item.InstanceId == "first" &&
                    item.Kind == "inactiveTaskReassigned");
                var outboxCount = await db.OutboxItems.CountAsync(item => item.InstanceId == "first" &&
                    item.PayloadJson.Contains("inactiveTaskReassigned"));
                check("离职状态、任务投影、事件链和待发项同事务保存",
                    task.AssigneeId == "manager" && eventCount == 1 && outboxCount == 1 &&
                    await service.VerifyEventChainAsync("first"));
            }

            var gate = new LoadGate();
            var departureService = new ApprovalRuntimeAppService(new GatedRepository(repository, gate));
            var approvalService = new ApprovalRuntimeAppService(new GatedRepository(repository, gate));
            var secondRef = new DepartureTaskRef { InstanceId = "second", TaskId = secondTask };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var departure = CaptureAsync(() => departureService.ReassignDepartedTaskAsync(secondRef,
                "user-n1", snapshot, occurredAt, "departure-second", timeout.Token));
            var approval = CaptureAsync(() => approvalService.ExecuteAsync(new RuntimeCommandInput
            {
                Command = new EngineCommand
                {
                    Kind = EngineCommandKinds.Approve,
                    InstanceId = "second",
                    TaskId = secondTask,
                    RequestId = "approve-second",
                },
                Context = EngineScenarioChecks.Context(graph),
            }, timeout.Token));
            var errors = await Task.WhenAll(departure, approval);
            check("审批与离职交接并发时只有一个旧版本提交", errors.Count(item => item is null) == 1 &&
                errors.Count(item => item is UserFriendlyException { Code: ErrorCodes.Conflict }) == 1);
            await using (var db = await factory.CreateDbContextAsync())
            {
                var instance = await db.Instances.SingleAsync(item => item.Id == "second");
                var departureEvents = await db.OperationEvents.CountAsync(item => item.InstanceId == "second" &&
                    item.Kind == "inactiveTaskReassigned");
                check("并发败者没有留下部分任务变更或孤立事件", instance.Version == 2 &&
                    departureEvents == (errors[0] is null ? 1 : 0) &&
                    await service.VerifyEventChainAsync("second"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 以已发布版本启动一个待办。
    /// </summary>
    /// <param name="service">运行时服务。</param>
    /// <param name="graph">发布图。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <returns>新待办 ID。</returns>
    private static async Task<string> StartAsync(ApprovalRuntimeAppService service, FlowExecutionGraph graph,
        string instanceId)
    {
        var result = await service.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = EngineCommandKinds.Start,
                InstanceId = instanceId,
                RequestId = "start-" + instanceId
            },
            Context = EngineScenarioChecks.Context(graph),
            StartInstance = new InstanceEntity
            {
                Id = instanceId,
                SerialNo = "AP-" + instanceId,
                FlowVersionId = "version",
                FlowCode = "DEPARTURE",
                BusinessKey = "BUSINESS-" + instanceId,
                Title = "在途审批",
                InitiatorId = "initiator",
                ClientAppCode = "demo",
            },
        });
        return result.State.Tasks.Single().Id;
    }

    /// <summary>
    /// 建立停用人员和在职上级快照。
    /// </summary>
    /// <returns>组织快照。</returns>
    private static AssigneeSnapshot Snapshot() => new()
    {
        People =
        [
            new AssigneePerson { Id = "user-n1", ManagerId = "manager", IsActive = false },
            new AssigneePerson { Id = "manager", IsActive = true },
        ],
    };

    /// <summary>
    /// 捕获并发执行中可预期的用户友好冲突。
    /// </summary>
    /// <param name="action">异步审批操作。</param>
    /// <returns>错误或空。</returns>
    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (UserFriendlyException error)
        {
            return error;
        }
    }

    /// <summary>
    /// 使用同一组选项建立独立的 EF 上下文。
    /// </summary>
    /// <param name="options">SQLite 配置。</param>
    private sealed class ContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 确保两条命令均读取旧版本后才开始提交。
    /// </summary>
    private sealed class LoadGate
    {
        /// <summary>
        /// 已到达读取点的命令数。
        /// </summary>
        private int arrivals;

        /// <summary>
        /// 双方到达后释放的闸门。
        /// </summary>
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 等待并发命令均读到旧状态。
        /// </summary>
        /// <param name="cancellationToken">超时标记。</param>
        /// <returns>异步等待。</returns>
        public Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                release.TrySetResult();
            }

            return release.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 在加载运行快照后安排并发闸门的测试仓储。
    /// </summary>
    /// <param name="inner">实际仓储。</param>
    /// <param name="gate">并发闸门。</param>
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
        public Task<IReadOnlyList<DepartureTaskRef>> ListDepartureTasksAsync(string assigneeId,
            CancellationToken cancellationToken = default) => inner.ListDepartureTasksAsync(assigneeId, cancellationToken);

        /// <inheritdoc />
        public Task<bool> CommitAsync(RuntimeCommit commit, CancellationToken cancellationToken = default) =>
            inner.CommitAsync(commit, cancellationToken);

        /// <inheritdoc />
        public Task<EventChainSnapshot?> LoadEventChainAsync(string instanceId,
            CancellationToken cancellationToken = default) => inner.LoadEventChainAsync(instanceId, cancellationToken);
    }
}
