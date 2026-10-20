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
            var parallelGraph = EngineScenarioChecks.GatewayGraph(FlowNodeTypes.ParallelGateway);
            var allGraph = ModeGraph("all");
            var anyGraph = ModeGraph("any");
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
                context.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "parallel-version",
                    FlowId = "flow",
                    Version = 2,
                    SchemaVersion = FlowSchema.CurrentVersion,
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(parallelGraph, ApprovalJson.Options),
                });
                context.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "all-version",
                    FlowId = "flow",
                    Version = 3,
                    SchemaVersion = FlowSchema.CurrentVersion,
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(allGraph, ApprovalJson.Options),
                });
                context.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "any-version",
                    FlowId = "flow",
                    Version = 4,
                    SchemaVersion = FlowSchema.CurrentVersion,
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(anyGraph, ApprovalJson.Options),
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

            var parallelStart = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = new EngineCommand
                {
                    Kind = EngineCommandKinds.Start,
                    RequestId = "parallel-start",
                    InstanceId = "parallel-instance",
                },
                Context = EngineScenarioChecks.Context(parallelGraph),
                StartInstance = new InstanceEntity
                {
                    Id = "parallel-instance",
                    SerialNo = "AP-0002",
                    FlowVersionId = "parallel-version",
                    FlowCode = "PURCHASE",
                    BusinessKey = "PO-002",
                    Title = "并行分支场景",
                    InitiatorId = "requester",
                    ClientAppCode = "demo",
                },
            });
            check("并行分支为两个人各创建一个待办", parallelStart.State.Tasks.Count == 2);
            var firstParallelCommand = new EngineCommand
            {
                Kind = EngineCommandKinds.Approve,
                RequestId = "parallel-approve-a",
                InstanceId = "parallel-instance",
                TaskId = parallelStart.State.Tasks[0].Id,
            };
            var secondParallelCommand = new EngineCommand
            {
                Kind = EngineCommandKinds.Approve,
                RequestId = "parallel-approve-b",
                InstanceId = "parallel-instance",
                TaskId = parallelStart.State.Tasks[1].Id,
            };
            var parallelGate = new LoadGate();
            var parallelServiceA = new ApprovalRuntimeAppService(
                new GatedRepository(new ApprovalRuntimeRepository(factory), parallelGate));
            var parallelServiceB = new ApprovalRuntimeAppService(
                new GatedRepository(new ApprovalRuntimeRepository(factory), parallelGate));
            var parallelOutcomes = await Task.WhenAll(
                CaptureAsync(() => parallelServiceA.ExecuteAsync(ApprovalInput(firstParallelCommand, parallelGraph))),
                CaptureAsync(() => parallelServiceB.ExecuteAsync(ApprovalInput(secondParallelCommand, parallelGraph))));
            check("两位审批人并发同意时只接受一份旧版本提交",
                parallelOutcomes.Count(error => error is null) == 1 &&
                parallelOutcomes.Count(error => error is UserFriendlyException { Code: ErrorCodes.Conflict }) == 1);
            var retryCommand = parallelOutcomes[0] is null ? secondParallelCommand : firstParallelCommand;
            var parallelDone = await service.ExecuteAsync(ApprovalInput(retryCommand, parallelGraph));
            await using (var context = await factory.CreateDbContextAsync())
            {
                check("冲突方刷新重试后并行分支仅汇合一次",
                    parallelDone.State.Status == EngineInstanceStatuses.Approved &&
                    await context.OperationEvents.CountAsync(item => item.InstanceId == "parallel-instance" &&
                        item.Kind == "instanceApproved") == 1);
            }

            await CheckMultiPersonModeAsync(check, factory, service, allGraph, "all", "会签");
            await CheckMultiPersonModeAsync(check, factory, service, anyGraph, "any", "或签");

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
    /// 检查同一节点的两位审批人并发提交及多人规则的最终结果。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <param name="factory">真实 SQLite 上下文工厂。</param>
    /// <param name="service">应用服务。</param>
    /// <param name="graph">多人审批执行图。</param>
    /// <param name="mode">多人审批方式。</param>
    /// <param name="name">方式的中文名称。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckMultiPersonModeAsync(Action<string, bool> check, TestContextFactory factory,
        ApprovalRuntimeAppService service, FlowExecutionGraph graph, string mode, string name)
    {
        var instanceId = mode + "-instance";
        var started = await service.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = EngineCommandKinds.Start,
                RequestId = mode + "-start",
                InstanceId = instanceId,
            },
            Context = ModeContext(graph),
            StartInstance = new InstanceEntity
            {
                Id = instanceId,
                SerialNo = "AP-" + mode,
                FlowVersionId = mode + "-version",
                FlowCode = "PURCHASE",
                BusinessKey = "PO-" + mode,
                Title = name + "场景",
                InitiatorId = "requester",
                ClientAppCode = "demo",
            },
        });
        check(name + "同一节点为两个人创建待办", started.State.Tasks.Count == 2 &&
            started.State.Tasks.All(item => item.NodeId == "n1"));

        var commands = started.State.Tasks.Select((task, index) => new EngineCommand
        {
            Kind = EngineCommandKinds.Approve,
            RequestId = mode + "-approve-" + index,
            InstanceId = instanceId,
            TaskId = task.Id,
        }).ToArray();
        var gate = new LoadGate();
        var services = Enumerable.Range(0, 2).Select(_ => new ApprovalRuntimeAppService(
            new GatedRepository(new ApprovalRuntimeRepository(factory), gate))).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var outcomes = await Task.WhenAll(services.Select((current, index) => CaptureAsync(() =>
            current.ExecuteAsync(new RuntimeCommandInput
            {
                Command = commands[index],
                Context = ModeContext(graph),
            }, timeout.Token))));
        check(name + "两人同节点并发仅一次旧版本提交成功",
            outcomes.Count(error => error is null) == 1 &&
            outcomes.Count(error => error is UserFriendlyException { Code: ErrorCodes.Conflict }) == 1);
        if (mode == "all")
        {
            var loser = outcomes[0] is null ? commands[1] : commands[0];
            var done = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = loser,
                Context = ModeContext(graph),
            });
            check("会签冲突方刷新重试后全员同意", done.State.Status == EngineInstanceStatuses.Approved &&
                done.State.Tasks.All(task => task.Status == EngineTaskStatuses.Approved));
        }

        await using var context = await factory.CreateDbContextAsync();
        var instance = await context.Instances.SingleAsync(item => item.Id == instanceId);
        var completionCount = await context.OperationEvents.CountAsync(item => item.InstanceId == instanceId &&
            item.Kind == "instanceApproved");
        check(name + "并发同意后审批单只结束一次",
            instance.Status == EngineInstanceStatuses.Approved && completionCount == 1 &&
            instance.Version == (mode == "all" ? 3 : 2));
    }

    /// <summary>
    /// 建立只有一个多人审批节点的执行图。
    /// </summary>
    /// <param name="mode">多人审批方式。</param>
    /// <returns>执行图。</returns>
    private static FlowExecutionGraph ModeGraph(string mode)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        var node = graph.Nodes.Single(item => item.Id == "n1");
        node.Assignee = new FlowAssignee { Source = "member", Value = "p1,p2" };
        node.Approval = new FlowApprovalSettings { Mode = mode };
        return graph;
    }

    /// <summary>
    /// 为同节点多人审批提供固定的组织快照。
    /// </summary>
    /// <param name="graph">执行图。</param>
    /// <returns>引擎上下文。</returns>
    private static EngineContext ModeContext(FlowExecutionGraph graph)
    {
        var context = EngineScenarioChecks.Context(graph);
        context.AssigneeSnapshot = new AssigneeSnapshot
        {
            InitiatorId = "requester",
            AdministratorId = "admin",
            People = [new AssigneePerson { Id = "requester" }, new AssigneePerson { Id = "p1" },
                new AssigneePerson { Id = "p2" }, new AssigneePerson { Id = "admin" }],
        };
        return context;
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
