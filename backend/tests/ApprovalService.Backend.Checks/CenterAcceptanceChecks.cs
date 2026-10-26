using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用真实 SQLite、JWT 和运行引擎检查发起人催办与安全进度投影。
/// </summary>
internal static class CenterAcceptanceChecks
{
    /// <summary>
    /// 验证当前人催办限流、未来节点隐藏与关联目标裁剪。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">场景断言入口。</param>
    /// <returns>检查完成。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            await using var server = new ServerFactory(backendRoot, directory,
                new Dictionary<string, string?> { ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]) });
            using var http = server.CreateClient();
            var tokens = await CenterChecksFixture.SeedAsync(server.Services);
            await SeedProgressAsync(server.Services);

            using var detailResponse = await SendAsync(http, HttpMethod.Get,
                CenterRoutes.Instances + "/a-todo", tokens.Local);
            var detail = await detailResponse.Content.ReadFromJsonAsync<CenterDetailResponse>();
            var related = detail?.Tasks.SingleOrDefault(task => task.Id == "a-todo-task")?.RelatedTargets ?? [];
            check("中心仅投影已到达和当前节点，不暴露未来审批节点与分支表达式",
                detailResponse.StatusCode == HttpStatusCode.OK && detail is not null &&
                detail.Progress.Nodes.Count == 2 && detail.Progress.Nodes.Any(node => node.IsActive) &&
                detail.Progress.Nodes.All(node => node.Name != "未来审批") &&
                detail.Progress.Edges.Count == 1 &&
                !(await detailResponse.Content.ReadAsStringAsync()).Contains("future-secret-condition", StringComparison.Ordinal));
            check("征询和减签目标只属于当前人员获准任务",
                related.Any(target => target.Action == OperationKinds.Reply && target.Id == "consult-for-viewer") &&
                related.Any(target => target.Action == OperationKinds.RemoveSigner && target.Id == "signer-for-viewer") &&
                related.All(target => target.Id != "consult-for-other"));

            using var remindDetail = await SendAsync(http, HttpMethod.Get,
                CenterRoutes.Instances + "/e-started", tokens.Local);
            var started = await remindDetail.Content.ReadFromJsonAsync<CenterDetailResponse>();
            using var first = await SendAsync(http, HttpMethod.Post,
                CenterRoutes.Instances + "/e-started/remind", tokens.Local);
            using var repeated = await SendAsync(http, HttpMethod.Post,
                CenterRoutes.Instances + "/e-started/remind", tokens.Local);
            using var stranger = await SendAsync(http, HttpMethod.Post,
                CenterRoutes.Instances + "/e-started/remind", tokens.Stranger);
            using var nonInitiator = await SendAsync(http, HttpMethod.Post,
                CenterRoutes.Instances + "/a-todo/remind", tokens.Local);
            check("发起人中心催办复用真实引擎一小时限制并阻止无权人员",
                started?.CanRemind == true && first.StatusCode == HttpStatusCode.NoContent &&
                repeated.StatusCode == HttpStatusCode.Conflict && stranger.StatusCode == HttpStatusCode.NotFound &&
                nonInitiator.StatusCode == HttpStatusCode.Forbidden);
            await using var verify = await server.Services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>()
                .CreateDbContextAsync();
            check("中心催办写入真实事件和通知待发队列",
                await verify.OperationEvents.AnyAsync(item => item.InstanceId == "e-started" && item.Kind == "reminderRequested") &&
                await verify.OutboxItems.AnyAsync(item => item.InstanceId == "e-started" && item.Kind == "notification"));
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 在隔离数据库中补齐真实催办任务、已到达路径和当前人的关联目标。
    /// </summary>
    /// <param name="services">测试宿主服务。</param>
    /// <returns>样本保存完成。</returns>
    private static async Task SeedProgressAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var version = await db.FlowVersions.SingleAsync(item => item.Id == "version");
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(version.GraphJson, ApprovalJson.Options)!;
        var definition = JsonSerializer.Deserialize<FlowDefinition>(version.DefinitionJson, ApprovalJson.Options)!;
        var current = graph.Nodes.First(node => node.Type == FlowNodeTypes.Approval);
        var future = graph.Nodes.Last(node => node.Type == FlowNodeTypes.Approval);
        future.Name = "未来审批";
        graph.Edges.First(edge => edge.SourceId == current.Id).Condition = "future-secret-condition";
        definition.Canvas.Nodes = graph.Nodes.Select((node, index) => new FlowNode
        {
            Id = node.Id,
            Type = node.Type,
            Name = node.Name,
            X = 100 + index * 180,
            Y = 140,
        }).ToList();
        definition.Canvas.Edges = graph.Edges.Select(edge => new FlowEdge
        {
            Id = edge.Id,
            SourceId = edge.SourceId,
            TargetId = edge.TargetId,
        }).ToList();
        // 测试只追加一份锁定版本，遵守正式发布版本不可修改的数据库约束。
        db.FlowVersions.Add(new ApprovalService.Domain.Runtime.FlowVersionEntity
        {
            Id = "version-progress",
            FlowId = version.FlowId,
            Version = 3,
            SchemaVersion = version.SchemaVersion,
            DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
            GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
        });
        var writable = await db.Instances.SingleAsync(item => item.Id == "a-todo");
        writable.FlowVersionId = "version-progress";
        var state = JsonSerializer.Deserialize<EngineState>(writable.StateJson, ApprovalJson.Options)!;
        state.Operations.Consultations.Add(new OperationConsultation
        {
            Id = "consult-for-viewer",
            TaskId = "a-todo-task",
            RecipientId = "viewer",
        });
        state.Operations.Consultations.Add(new OperationConsultation
        {
            Id = "consult-for-other",
            TaskId = "a-todo-task",
            RecipientId = "other",
        });
        state.Tasks.Add(new EngineTask
        {
            Id = "signer-for-viewer",
            TokenId = "a-todo-token",
            NodeId = current.Id,
            AssigneeId = "other",
            AddedByTaskId = "a-todo-task",
            Status = EngineTaskStatuses.Pending,
        });
        writable.StateJson = JsonSerializer.Serialize(state, ApprovalJson.Options);
        // 事件只记已实际到达的两个节点，后续审批及其条件不得进入进度响应。
        var start = graph.Nodes.Single(node => node.Type == FlowNodeTypes.Start);
        foreach (var (sequence, nodeId) in new[] { (3L, start.Id), (4L, current.Id) })
        {
            db.OperationEvents.Add(new ApprovalService.Domain.Runtime.OperationEventEntity
            {
                Id = "visited-" + sequence,
                InstanceId = "a-todo",
                Sequence = sequence,
                Kind = "nodeEntered",
                OccurredAt = DateTimeOffset.UtcNow,
                PayloadJson = JsonSerializer.Serialize(new EngineEvent { Kind = "nodeEntered", NodeId = nodeId }, ApprovalJson.Options),
            });
        }
        writable.LastEventSequence = 4;
        var started = await db.Instances.SingleAsync(item => item.Id == "e-started");
        // 原中心列表样本只用于查询；运行命令前补齐可验证的历史事件锚点。
        var initialEvent = await db.OperationEvents.AsNoTracking().SingleAsync(item => item.InstanceId == "e-started");
        var initialHash = OperationHash.Compute(initialEvent);
        await db.OperationEvents.Where(item => item.Id == initialEvent.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Hash, initialHash));
        started.LastEventSequence = initialEvent.Sequence;
        started.LastEventHash = initialHash;
        started.FlowVersionId = "version-progress";
        started.StateJson = JsonSerializer.Serialize(new EngineState
        {
            InstanceId = "e-started",
            Status = EngineInstanceStatuses.Running,
            Tokens = [new EngineToken { Id = "e-started-token", NodeId = current.Id, WaitingFor = "task" }],
            Tasks = [new EngineTask { Id = "e-started-task", TokenId = "e-started-token",
                NodeId = current.Id, AssigneeId = "other", Status = EngineTaskStatuses.Pending,
                CompletionKind = EngineCommandKinds.Approve }],
            Operations = new OperationState { InitiatorId = "viewer", FlowCode = "CENTER" },
        }, ApprovalJson.Options);
        db.ApprovalTasks.Add(new ApprovalService.Domain.Runtime.ApprovalTaskEntity
        {
            Id = "e-started-task",
            InstanceId = "e-started",
            NodeId = current.Id,
            AssigneeId = "other",
            Status = EngineTaskStatuses.Pending,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 用指定 JWT 请求中心接口，写请求有独立的稳定 ID。
    /// </summary>
    /// <param name="http">进程内 HTTP 客户端。</param>
    /// <param name="method">请求方法。</param>
    /// <param name="path">接口地址。</param>
    /// <param name="token">当前人员令牌。</param>
    /// <returns>HTTP 响应。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, string token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(HeaderNames.RequestId, Guid.NewGuid().ToString("N"));
        return await http.SendAsync(request);
    }
}
