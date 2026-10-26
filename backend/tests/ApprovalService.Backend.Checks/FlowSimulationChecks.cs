using System.Text.Json;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 检查模拟会话只使用纯引擎、假人员和内存状态。
/// </summary>
internal static class FlowSimulationChecks
{
    /// <summary>
    /// 执行最短审批、授权、删除和闲置过期场景。
    /// </summary>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var clock = new SimulationClock();
        var repository = new SimulationFlowRepository();
        var store = new SimulationStore();
        var service = new FlowSimulationAppService(new FlowManagementAppService(repository, clock), store, clock);
        var definition = Definition();
        var request = new FlowSimulationCreateRequest
        {
            Definition = definition,
            Data = JsonSerializer.SerializeToElement(new { }),
            InitiatorId = "fake-initiator",
            FakeAssignees = new(StringComparer.Ordinal)
            {
                ["approval"] = ["fake-approver"],
            },
        };

        var first = await service.CreateAsync(request, "admin", true);
        check("未发布草稿在内存启动且只生成假待办", first.Status == EngineInstanceStatuses.Running &&
            first.Tasks.Count == 1 && first.Tasks[0].AssigneeId == "fake-approver" && store.Count == 1);
        check("模拟没有持久化或出站依赖", repository.Reads > 0 && repository.Writes == 0 &&
            first.Events.Any(item => item.Kind == "taskCreated"));

        await ExpectAsync(() => service.GetAsync(first.Id, "other", true), ErrorCodes.NotFound,
            "他人的模拟会话不可读取", check);
        await ExpectAsync(() => service.GetAsync(first.Id, "admin", false), ErrorCodes.Forbidden,
            "嵌入登录不能读取模拟", check);
        await ExpectAsync(() => service.StepAsync(first.Id, new FlowSimulationStepRequest
        {
            Kind = EngineCommandKinds.Approve,
            TaskId = first.Tasks[0].Id,
            ActorId = "wrong-person",
        }, "admin", true), ErrorCodes.ValidationFailed, "不能冒充另一名假处理人", check);

        var final = await service.StepAsync(first.Id, new FlowSimulationStepRequest
        {
            Kind = EngineCommandKinds.Approve,
            TaskId = first.Tasks[0].Id,
            ActorId = "fake-approver",
        }, "admin", true);
        check("假审批经统一引擎走到已通过", final.Status == EngineInstanceStatuses.Approved &&
            final.Events.Any(item => item.Kind == "instanceApproved"));

        await service.DeleteAsync(first.Id, "admin", true);
        check("删除立即清除内存会话", store.Count == 0);

        var expiring = await service.CreateAsync(request, "admin", true);
        clock.Advance(TimeSpan.FromMinutes(31));
        await ExpectAsync(() => service.GetAsync(expiring.Id, "admin", true), ErrorCodes.NotFound,
            "闲置超过三十分钟不可恢复", check);
        check("过期会话从内存清除", store.Count == 0);

        var automatic = Definition();
        automatic.Canvas.Nodes[1] = new FlowNode
        {
            Id = "approval",
            Type = FlowNodeTypes.Automatic,
            Name = "假自动动作",
            Automation = new FlowAutomationSettings { ActionCode = "fake-action" },
        };
        var waiting = await service.CreateAsync(new FlowSimulationCreateRequest
        {
            Definition = automatic,
            Data = JsonSerializer.SerializeToElement(new { }),
            InitiatorId = "fake-initiator",
        }, "admin", true);
        check("自动动作只等待显式假结果", waiting.Status == EngineInstanceStatuses.Running &&
            waiting.Waits is [{ Kind: "action" }] &&
            waiting.Events.Any(item => item.Kind == "actionPending") && repository.Writes == 0);
        var afterAction = await service.StepAsync(waiting.Id, new FlowSimulationStepRequest
        {
            Kind = EngineCommandKinds.ActionSucceeded,
            TokenId = waiting.Waits[0].TokenId,
        }, "admin", true);
        check("假自动动作结果经统一引擎完成", afterAction.Status == EngineInstanceStatuses.Approved);

        var subprocess = Definition();
        subprocess.Canvas.Nodes[1] = new FlowNode
        {
            Id = "approval",
            Type = FlowNodeTypes.Subprocess,
            Name = "假子流程",
            Subprocess = new FlowSubprocessSettings { FlowCode = "CHILD", WaitForCompletion = true },
        };
        var childWaiting = await service.CreateAsync(new FlowSimulationCreateRequest
        {
            Definition = subprocess,
            Data = JsonSerializer.SerializeToElement(new { }),
            InitiatorId = "fake-initiator",
        }, "admin", true);
        check("子流程只等待显式假完成", childWaiting.Status == EngineInstanceStatuses.Running &&
            childWaiting.Waits is [{ Kind: "subprocess" }]);
        var afterChild = await service.StepAsync(childWaiting.Id, new FlowSimulationStepRequest
        {
            Kind = EngineCommandKinds.SubprocessCompleted,
            TokenId = childWaiting.Waits[0].TokenId,
        }, "admin", true);
        check("假子流程完成由统一引擎推进", afterChild.Status == EngineInstanceStatuses.Approved);

        var attachmentFlow = Definition();
        attachmentFlow.Data.Fields.Add(new FlowFieldDefinition
        {
            Key = "document",
            Name = "假文件",
            Type = FlowFieldTypes.Attachment,
            Required = true,
        });
        var attached = await service.CreateAsync(new FlowSimulationCreateRequest
        {
            Definition = attachmentFlow,
            Data = JsonSerializer.SerializeToElement(new { document = new[] { "fake-file-id" } }),
            InitiatorId = "fake-initiator",
            FakeAssignees = new(StringComparer.Ordinal) { ["approval"] = ["fake-approver"] },
        }, "admin", true);
        check("必填附件可用假文件 ID 模拟且不写真实存储", attached.Tasks.Count == 1 &&
            repository.Writes == 0);

        var root = Directory.GetParent(Fixtures.BackendRoot())!.FullName;
        var qualityPath = Path.Combine(root, "frontend", "src", "designer", "workbench", "samples",
            "quality-exception.json");
        var quality = JsonSerializer.Deserialize<FlowDefinition>(await File.ReadAllTextAsync(qualityPath),
            ApprovalJson.Options) ?? throw new InvalidOperationException("质量异常模板为空。");
        quality.Code = "SIMULATION";
        foreach (var node in quality.Canvas.Nodes.Where(node => node.Approval is not null))
        {
            // 此场景只验证假字段路由；必填意见在独立的模拟意见场景中验证。
            node.Approval!.CommentRequired = false;
        }

        var fakePeople = quality.Canvas.Nodes.Where(node => node.Type is FlowNodeTypes.Approval or
            FlowNodeTypes.Handling or FlowNodeTypes.Cc).ToDictionary(node => node.Id,
            node => new List<string> { "fake-" + node.Id }, StringComparer.Ordinal);
        var qualityStart = await service.CreateAsync(new FlowSimulationCreateRequest
        {
            Definition = quality,
            Data = JsonSerializer.SerializeToElement(new
            {
                title = "异常",
                lotNumber = "LOT-1",
                defectDescription = "尺寸偏差",
            }),
            InitiatorId = "fake-initiator",
            FakeAssignees = fakePeople,
        }, "admin", true);
        check("模拟发起可等待后续质检员填写分支字段", qualityStart.Tasks.Any(task =>
            task.NodeId == "inspection" && task.Status == EngineTaskStatuses.Pending));
        var inspectionTask = qualityStart.Tasks.Single(task => task.NodeId == "inspection" &&
            task.Status == EngineTaskStatuses.Pending);
        await ExpectAsync(() => service.StepAsync(qualityStart.Id, new FlowSimulationStepRequest
        {
            Kind = inspectionTask.CompletionKind,
            TaskId = inspectionTask.Id,
            ActorId = inspectionTask.AssigneeId,
            FieldValues = JsonSerializer.SerializeToElement(new { title = "伪造业务标题" }),
        }, "admin", true), ErrorCodes.ValidationFailed, "模拟不能借审批人字段覆盖业务数据", check);
        await ExpectAsync(() => service.StepAsync(qualityStart.Id, new FlowSimulationStepRequest
        {
            Kind = inspectionTask.CompletionKind,
            TaskId = inspectionTask.Id,
            ActorId = inspectionTask.AssigneeId,
        }, "admin", true), ErrorCodes.ValidationFailed, "到达网关仍缺质检结论时不走默认路径", check);
        var qualityState = qualityStart;
        var qualitySteps = 0;
        while (qualityState.Status == EngineInstanceStatuses.Running && qualitySteps++ < 25)
        {
            var task = qualityState.Tasks.FirstOrDefault(item => item.Status == EngineTaskStatuses.Pending);
            if (task is not null)
            {
                qualityState = await service.StepAsync(qualityState.Id, new FlowSimulationStepRequest
                {
                    Kind = task.CompletionKind,
                    TaskId = task.Id,
                    ActorId = task.AssigneeId,
                    FieldValues = task.NodeId == "inspection" ? JsonSerializer.SerializeToElement(new
                    {
                        reworkDecision = "no",
                    }) : null,
                }, "admin", true);
                continue;
            }

            var wait = qualityState.Waits.SingleOrDefault()
                ?? throw new InvalidOperationException("模拟质量异常没有可推进的假任务或动作。");
            qualityState = await service.StepAsync(qualityState.Id, new FlowSimulationStepRequest
            {
                Kind = EngineCommandKinds.ActionSucceeded,
                TokenId = wait.TokenId,
            }, "admin", true);
        }

        check("模拟质检填写决定后续分支并走完，始终不写持久仓储", qualityState.Status ==
            EngineInstanceStatuses.Approved && qualitySteps < 25 && repository.Writes == 0);
        await service.DeleteAsync(qualityState.Id, "admin", true);
    }

    /// <summary>
    /// 构造没有真实业务字段的三节点草稿。
    /// </summary>
    /// <returns>可编译流程。</returns>
    private static FlowDefinition Definition() => new()
    {
        Code = "SIMULATION",
        Name = "测试审批",
        Canvas = new FlowCanvas
        {
            Nodes =
            [
                new FlowNode { Id = "start", Type = FlowNodeTypes.Start, Name = "开始" },
                new FlowNode
                {
                    Id = "approval", Type = FlowNodeTypes.Approval, Name = "审批",
                    Assignee = new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "real-person" },
                    Approval = new FlowApprovalSettings { AllowedActions = [EngineCommandKinds.Approve] },
                },
                new FlowNode { Id = "end", Type = FlowNodeTypes.End, Name = "结束" },
            ],
            Edges =
            [
                new FlowEdge { Id = "first", SourceId = "start", TargetId = "approval" },
                new FlowEdge { Id = "last", SourceId = "approval", TargetId = "end" },
            ],
        },
    };

    /// <summary>
    /// 核对统一用户错误码。
    /// </summary>
    /// <param name="action">应失败的异步操作。</param>
    /// <param name="expected">预期错误码。</param>
    /// <param name="name">检查名称。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task ExpectAsync(Func<Task> action, string expected, string name,
        Action<string, bool> check)
    {
        try
        {
            await action();
            check(name, false);
        }
        catch (UserFriendlyException error)
        {
            check(name, error.Code == expected);
        }
    }

    /// <summary>
    /// 可推进的确定时钟。
    /// </summary>
    private sealed class SimulationClock : TimeProvider
    {
        /// <summary>
        /// 当前假时间。
        /// </summary>
        private DateTimeOffset now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;

        /// <summary>
        /// 推进假时间。
        /// </summary>
        /// <param name="duration">推进量。</param>
        public void Advance(TimeSpan duration) => now += duration;
    }

    /// <summary>
    /// 仅允许读取一个流程的假仓储。
    /// </summary>
    private sealed class SimulationFlowRepository : IFlowManagementRepository
    {
        /// <summary>
        /// 读取次数。
        /// </summary>
        public int Reads
        {
            get; private set;
        }

        /// <summary>
        /// 写入次数。
        /// </summary>
        public int Writes
        {
            get; private set;
        }

        /// <inheritdoc />
        public Task<FlowManagerIdentity> LoadIdentityAsync(string personId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(new FlowManagerIdentity
            {
                IsActive = true,
                IsOrganizationAdmin = personId == "admin",
            });
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<FlowDepartmentOption>> ListManageableDepartmentsAsync(string actorId,
            bool isLocalSession, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("模拟不应列出持久部门。");

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot?> LoadAsync(string code, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<ManagedFlowSnapshot?>(code == "SIMULATION" ? new ManagedFlowSnapshot
            {
                Flow = new FlowEntity { Code = code, Name = "测试审批" },
            } : null);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ManagedFlowSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("模拟不应列出持久流程。");

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot> CreateAsync(FlowDraftWrite input, CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new InvalidOperationException("模拟不得创建持久流程。");
        }

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot> ReplaceDraftAsync(FlowDraftWrite input, CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new InvalidOperationException("模拟不得保存草稿。");
        }

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot> PublishAsync(FlowPublishWrite input, CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new InvalidOperationException("模拟不得发布流程。");
        }

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot> SetEnabledAsync(FlowEnabledWrite input, CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new InvalidOperationException("模拟不得启停流程。");
        }
    }

    /// <summary>
    /// 检查用内存存储。
    /// </summary>
    private sealed class SimulationStore : IFlowSimulationSessionStore
    {
        /// <summary>
        /// 会话表。
        /// </summary>
        private readonly Dictionary<string, FlowSimulationSession> sessions = new(StringComparer.Ordinal);

        /// <summary>
        /// 当前会话数。
        /// </summary>
        public int Count => sessions.Count;

        /// <inheritdoc />
        public void Add(FlowSimulationSession session, DateTimeOffset now) => sessions.Add(session.Id, session);

        /// <inheritdoc />
        public FlowSimulationSession? Find(string id) => sessions.GetValueOrDefault(id);

        /// <inheritdoc />
        public void Remove(string id) => sessions.Remove(id);
    }
}
