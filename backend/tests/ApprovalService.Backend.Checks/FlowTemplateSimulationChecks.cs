using System.Text.Json;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Definitions;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用设计器实际十模板和字段样例逐个检查发布编译与完整内存流转。
/// </summary>
internal static class FlowTemplateSimulationChecks
{
    /// <summary>
    /// 对十份正式模板运行同一发布编译器和模拟应用服务。
    /// </summary>
    /// <param name="repositoryRoot">仓库根目录。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步场景完成。</returns>
    public static async Task RunAsync(string repositoryRoot, Action<string, bool> check)
    {
        var path = Path.Combine(repositoryRoot, "backend", "tests", "ApprovalService.Backend.Checks",
            "Fixtures", "flow-templates.json");
        var fixtures = JsonSerializer.Deserialize<List<FlowTemplateFixture>>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        check("十模板夹具由正式设计器定义生成", fixtures.Count == 10 &&
            fixtures.Select(item => item.Definition.Code).Distinct(StringComparer.Ordinal).Count() == 10);

        var repository = new TemplateFlowRepository(fixtures.Select(item => item.Definition.Code));
        var sessions = new TemplateSessionStore();
        var service = new FlowSimulationAppService(new FlowManagementAppService(repository, TimeProvider.System),
            sessions, TimeProvider.System);
        var failures = new List<string>();
        foreach (var fixture in fixtures)
        {
            var definition = fixture.Definition;
            FlowSimulationResponse? response = null;
            try
            {
                var compiled = FlowCompiler.Compile(definition);
                check($"模板 {definition.Code} 可通过正式发布编译", compiled.Graph is not null &&
                    compiled.Issues.Count == 0);
                var assignees = definition.Canvas.Nodes.Where(node => node.Type is FlowNodeTypes.Approval or
                    FlowNodeTypes.Handling or FlowNodeTypes.Cc).ToDictionary(node => node.Id,
                    node => new List<string> { "fake-" + node.Id }, StringComparer.Ordinal);
                response = await service.CreateAsync(new FlowSimulationCreateRequest
                {
                    Definition = definition,
                    Data = fixture.Data,
                    InitiatorId = "fake-initiator",
                    FakeAssignees = assignees,
                }, "admin", true);

                // 使用实际返回的任务和令牌逐步推进；静态样例若落入回路，步骤上限会暴露问题。
                var steps = 0;
                while (response.Status == EngineInstanceStatuses.Running && steps++ < 100)
                {
                    var task = response.Tasks.FirstOrDefault(item => item.Status == EngineTaskStatuses.Pending);
                    if (task is not null)
                    {
                        response = await service.StepAsync(response.Id, new FlowSimulationStepRequest
                        {
                            Kind = task.CompletionKind,
                            TaskId = task.Id,
                            ActorId = task.AssigneeId,
                            Opinion = "测试模式模拟同意",
                        }, "admin", true);
                        continue;
                    }

                    var wait = response.Waits.FirstOrDefault();
                    if (wait is null)
                    {
                        break;
                    }

                    response = await service.StepAsync(response.Id, new FlowSimulationStepRequest
                    {
                        Kind = wait.Kind == "subprocess" ? EngineCommandKinds.SubprocessCompleted :
                            EngineCommandKinds.ActionSucceeded,
                        TokenId = wait.TokenId,
                        Success = true,
                    }, "admin", true);
                }

                check($"模板 {definition.Code} 可用假数据走完同一模拟引擎", response.Status ==
                    EngineInstanceStatuses.Approved && response.Events.Any(item => item.Kind == "instanceApproved") &&
                    steps <= 100);
            }
            catch (Exception error)
            {
                failures.Add($"{definition.Code}: {error.GetType().Name}: {error.Message}");
            }
            finally
            {
                if (response is not null)
                {
                    await service.DeleteAsync(response.Id, "admin", true);
                }
            }
        }

        check("十模板模拟结束后没有写持久流程或留下内存会话", repository.Writes == 0 &&
            sessions.Count == 0);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException("十模板验收失败：" + string.Join("；", failures));
        }
    }

    /// <summary>
    /// 与设计器模板原值同步的可序列化验收夹具。
    /// </summary>
    private sealed class FlowTemplateFixture
    {
        /// <summary>
        /// 原始完整流程定义。
        /// </summary>
        public FlowDefinition Definition { get; set; } = new();

        /// <summary>
        /// 按正式字段样例生成的假业务数据。
        /// </summary>
        public JsonElement Data
        {
            get; set;
        }
    }

    /// <summary>
    /// 只允许读取夹具编码的流程授权仓储，任何持久写入都直接失败。
    /// </summary>
    /// <param name="codes">已登记模板编码。</param>
    private sealed class TemplateFlowRepository(IEnumerable<string> codes) : IFlowManagementRepository
    {
        /// <summary>
        /// 可授权的模板编码。
        /// </summary>
        private readonly HashSet<string> knownCodes = new(codes, StringComparer.Ordinal);

        /// <summary>
        /// 持久写入调用次数。
        /// </summary>
        public int Writes
        {
            get; private set;
        }

        /// <inheritdoc />
        public Task<FlowManagerIdentity> LoadIdentityAsync(string personId,
            CancellationToken cancellationToken = default) => Task.FromResult(new FlowManagerIdentity
            {
                IsActive = true,
                IsOrganizationAdmin = personId == "admin",
            });

        /// <inheritdoc />
        public Task<IReadOnlyList<FlowDepartmentOption>> ListManageableDepartmentsAsync(string actorId,
            bool isLocalSession, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("模拟不得列出持久部门。");

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot?> LoadAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedFlowSnapshot?>(knownCodes.Contains(code) ? new ManagedFlowSnapshot
            {
                Flow = new FlowEntity { Code = code, Name = code },
            } : null);

        /// <inheritdoc />
        public Task<IReadOnlyList<ManagedFlowSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("模拟不得列出持久流程。");

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot> CreateAsync(FlowDraftWrite input,
            CancellationToken cancellationToken = default) => RefuseWrite();

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot> ReplaceDraftAsync(FlowDraftWrite input,
            CancellationToken cancellationToken = default) => RefuseWrite();

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot> PublishAsync(FlowPublishWrite input,
            CancellationToken cancellationToken = default) => RefuseWrite();

        /// <inheritdoc />
        public Task<ManagedFlowSnapshot> SetEnabledAsync(FlowEnabledWrite input,
            CancellationToken cancellationToken = default) => RefuseWrite();

        /// <summary>
        /// 任何写入都违背模拟隔离约束。
        /// </summary>
        /// <returns>总是抛错。</returns>
        private Task<ManagedFlowSnapshot> RefuseWrite()
        {
            Writes++;
            throw new InvalidOperationException("模拟不得写持久流程。");
        }
    }

    /// <summary>
    /// 验收时只保存于内存的会话容器。
    /// </summary>
    private sealed class TemplateSessionStore : IFlowSimulationSessionStore
    {
        /// <summary>
        /// 活跃会话。
        /// </summary>
        private readonly Dictionary<string, FlowSimulationSession> sessions = new(StringComparer.Ordinal);

        /// <summary>
        /// 当前活跃会话数。
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
