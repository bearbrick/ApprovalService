using System.Text.Json;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Definitions;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实质量异常模板验证审批人填写字段影响后续网关的正式运行路径。
/// </summary>
internal static class DynamicConditionChecks
{
    /// <summary>
    /// 发起时留空质检结论，先走返工回路，再改为无需返工并走到终态。
    /// </summary>
    /// <param name="repositoryRoot">仓库根目录。</param>
    /// <param name="check">场景断言入口。</param>
    /// <returns>场景执行完毕。</returns>
    public static async Task RunAsync(string repositoryRoot, Action<string, bool> check)
    {
        var path = Path.Combine(repositoryRoot, "frontend", "src", "designer", "workbench", "samples",
            "quality-exception.json");
        var definition = JsonSerializer.Deserialize<FlowDefinition>(await File.ReadAllTextAsync(path),
            ApprovalJson.Options) ?? throw new InvalidOperationException("质量异常模板为空。");
        var compiled = FlowCompiler.Compile(definition);
        check("质量异常模板通过正式发布编译", compiled.Graph is not null && compiled.Issues.Count == 0);
        var graph = compiled.Graph!;
        // 回路重审需要模板显式配置每次审批；模板库的修正由原任务同步。
        graph.Nodes.Single(node => node.Id == "inspection").Approval!.DuplicateAssigneeBehavior =
            FlowDuplicateAssigneeBehaviors.EveryTime;
        var assignees = FakeAssignees(graph);
        var data = JsonSerializer.SerializeToElement(new
        {
            title = "批次异常",
            lotNumber = "LOT-1",
            defectDescription = "尺寸偏差",
        });
        var repository = new MemoryRuntimeRepository(definition, graph);
        var runtime = new ApprovalRuntimeAppService(repository);
        var instance = new InstanceEntity
        {
            Id = "quality-dynamic",
            FlowCode = definition.Code,
            FlowVersionId = "quality-version",
            ClientAppCode = "fake-app",
            InitiatorId = "fake-initiator",
            ApprovalDataJson = data.GetRawText(),
            RoutingInputsJson = "{}",
        };
        var started = await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = EngineCommandKinds.Start,
                InstanceId = instance.Id,
                RequestId = "start",
            },
            Context = Context(assignees, 0),
            StartInstance = instance,
        });
        var inspection = started.State.Tasks.Single(task => task.Status == EngineTaskStatuses.Pending);
        check("正式运行从空审批人字段进入质检任务", inspection.NodeId == "inspection" &&
            started.State.Status == EngineInstanceStatuses.Running);

        var invalidActor = false;
        try
        {
            await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = Decide(instance.Id, "invalid", inspection, "someone-else", "yes"),
                Context = Context(assignees, 1),
            });
        }
        catch (UserFriendlyException error) when (error.Code is ErrorCodes.Forbidden or ErrorCodes.Conflict)
        {
            invalidActor = true;
        }

        check("被拒绝的填写不进入当前值或后续路由", invalidActor &&
            repository.CurrentState().Operations.CurrentApproverFieldValues.Count == 0);
        var rework = await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = Decide(instance.Id, "rework-yes", inspection, inspection.AssigneeId, "yes"),
            Context = Context(assignees, 2),
        });
        check("正式运行按本次质检填写进入返工", rework.State.Tasks.Any(task =>
            task.NodeId == "rework" && task.Status == EngineTaskStatuses.Pending) &&
            rework.State.Operations.CurrentApproverFieldValues["reworkDecision"].GetString() == "yes");

        var state = rework.State;
        var step = 3;
        var sawSecondInspection = false;
        while (state.Status == EngineInstanceStatuses.Running && step < 35)
        {
            var task = state.Tasks.FirstOrDefault(item => item.Status == EngineTaskStatuses.Pending);
            if (task is null)
            {
                var waiting = state.Tokens.SingleOrDefault(token => token.WaitingFor == "action")
                    ?? throw new InvalidOperationException("质量异常流程没有可推进的假待办或动作。");
                state = (await runtime.ExecuteAsync(new RuntimeCommandInput
                {
                    Command = new EngineCommand
                    {
                        Kind = EngineCommandKinds.ActionSucceeded,
                        InstanceId = instance.Id,
                        TokenId = waiting.Id,
                        RequestId = "action-" + step,
                    },
                    Context = Context(assignees, step),
                })).State;
                step++;
                continue;
            }
            var decision = task.NodeId == "inspection" ? "no" : null;
            sawSecondInspection |= task.NodeId == "inspection";
            state = (await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = Decide(instance.Id, "step-" + step, task, task.AssigneeId, decision),
                Context = Context(assignees, step),
            })).State;
            step++;
        }

        check("返工后改填无需返工，正式运行可走完并保留最新值", sawSecondInspection &&
            state.Status == EngineInstanceStatuses.Approved &&
            state.Operations.CurrentApproverFieldValues["reworkDecision"].GetString() == "no");

        var rejectRepository = new MemoryRuntimeRepository(definition, graph);
        var rejectRuntime = new ApprovalRuntimeAppService(rejectRepository);
        var rejectedInstance = new InstanceEntity
        {
            Id = "quality-rejected",
            FlowCode = definition.Code,
            FlowVersionId = "quality-version",
            ClientAppCode = "fake-app",
            InitiatorId = "fake-initiator",
            ApprovalDataJson = data.GetRawText(),
            RoutingInputsJson = "{}",
        };
        var rejectStart = await rejectRuntime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = EngineCommandKinds.Start,
                InstanceId = rejectedInstance.Id,
                RequestId = "start-reject",
            },
            Context = Context(assignees, 0),
            StartInstance = rejectedInstance,
        });
        var rejectedTask = rejectStart.State.Tasks.Single(task => task.Status == EngineTaskStatuses.Pending);
        var rejected = await rejectRuntime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = EngineCommandKinds.Reject,
                InstanceId = rejectedInstance.Id,
                RequestId = "reject-with-field",
                TaskId = rejectedTask.Id,
                Operation = new OperationInput
                {
                    ActorId = rejectedTask.AssigneeId,
                    Opinion = "拒绝",
                    FieldValues = JsonSerializer.SerializeToElement(new { reworkDecision = "yes" }),
                },
            },
            Context = Context(assignees, 1),
        });
        check("拒绝时提交的字段不成为当前值或后续网关依据", rejected.State.Status ==
            EngineInstanceStatuses.Rejected && rejected.State.Operations.CurrentApproverFieldValues.Count == 0);
    }

    /// <summary>
    /// 用假业务取人结果替代组织和外部系统。
    /// </summary>
    /// <param name="graph">编译后的正式执行图。</param>
    /// <returns>只含假人的快照。</returns>
    private static AssigneeSnapshot FakeAssignees(FlowExecutionGraph graph)
    {
        var snapshot = new AssigneeSnapshot { InitiatorId = "fake-initiator" };
        foreach (var node in graph.Nodes.Where(node => node.Type is FlowNodeTypes.Approval or
            FlowNodeTypes.Handling or FlowNodeTypes.Cc))
        {
            node.Assignee = new FlowAssignee { Source = FlowAssigneeSources.BusinessApi, Value = node.Id };
            snapshot.BusinessApiResults[node.Id] = ["fake-" + node.Id];
            snapshot.People.Add(new AssigneePerson { Id = "fake-" + node.Id });
        }

        snapshot.People.Add(new AssigneePerson { Id = "fake-initiator" });
        return snapshot;
    }

    /// <summary>
    /// 为一次正式命令提供可信假处理人和固定时刻。
    /// </summary>
    /// <param name="snapshot">假人员快照。</param>
    /// <param name="step">固定分钟偏移。</param>
    /// <returns>引擎上下文。</returns>
    private static EngineContext Context(AssigneeSnapshot snapshot, int step) => new()
    {
        AssigneeSnapshot = snapshot,
        OccurredAt = new DateTimeOffset(2026, 10, 26, 0, step, 0, TimeSpan.Zero),
        Operations = new OperationContext { InitiatorId = "fake-initiator" },
    };

    /// <summary>
    /// 构建带可选质检结论的人工任务命令。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="requestId">幂等请求 ID。</param>
    /// <param name="task">正式引擎待办。</param>
    /// <param name="actorId">假操作人。</param>
    /// <param name="decision">本次质检填写值。</param>
    /// <returns>人工命令。</returns>
    private static EngineCommand Decide(string instanceId, string requestId, EngineTask task,
        string actorId, string? decision) => new()
        {
            Kind = task.CompletionKind,
            InstanceId = instanceId,
            RequestId = requestId,
            TaskId = task.Id,
            Operation = new OperationInput
            {
                ActorId = actorId,
                Opinion = "假处理意见",
                FieldValues = decision is null ? null : JsonSerializer.SerializeToElement(new
                {
                    reworkDecision = decision,
                }),
            },
        };

    /// <summary>
    /// 仅在内存中保存正式应用服务的提交结果。
    /// </summary>
    /// <param name="definition">锁定定义。</param>
    /// <param name="graph">锁定执行图。</param>
    private sealed class MemoryRuntimeRepository(FlowDefinition definition, FlowExecutionGraph graph) :
        IApprovalRuntimeRepository
    {
        /// <summary>
        /// 当前已提交的审批单快照。
        /// </summary>
        private RuntimeSnapshot? stored;

        /// <summary>
        /// 锁定的发布版本。
        /// </summary>
        private readonly FlowVersionEntity version = new()
        {
            Id = "quality-version",
            FlowId = "quality-flow",
            Version = 1,
            DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
            GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
        };

        /// <inheritdoc />
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        /// <inheritdoc />
        public Task<RuntimeSnapshot?> LoadAsync(string instanceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(instanceId == stored?.Instance.Id ? stored : null);

        /// <inheritdoc />
        public Task<FlowVersionEntity?> LoadVersionAsync(string versionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FlowVersionEntity?>(versionId == version.Id ? version : null);

        /// <inheritdoc />
        public Task<IReadOnlyList<DepartureTaskRef>> ListDepartureTasksAsync(string assigneeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DepartureTaskRef>>([]);

        /// <inheritdoc />
        public Task<bool> CommitAsync(RuntimeCommit commit, CancellationToken cancellationToken = default)
        {
            stored = new RuntimeSnapshot
            {
                Instance = commit.Instance,
                FlowVersion = version,
                Tasks = commit.Tasks,
                LastEvent = commit.Events.LastOrDefault() ?? stored?.LastEvent,
            };
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task<EventChainSnapshot?> LoadEventChainAsync(string instanceId,
            CancellationToken cancellationToken = default) => Task.FromResult<EventChainSnapshot?>(null);

        /// <summary>
        /// 读取最后一次成功提交的纯引擎状态。
        /// </summary>
        /// <returns>已保存状态。</returns>
        public EngineState CurrentState() => JsonSerializer.Deserialize<EngineState>(
            stored?.Instance.StateJson ?? throw new InvalidOperationException("尚未提交审批单。"),
            ApprovalJson.Options) ?? throw new InvalidOperationException("已保存状态为空。");
    }
}
