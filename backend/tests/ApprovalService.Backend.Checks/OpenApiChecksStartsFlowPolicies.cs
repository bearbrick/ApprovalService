using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
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
/// 锁定发布图中的撤回限制与退回重提方式的 HTTP 和持久化场景。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 验证策略在发起时固定、旧图缺省以及退回后按原节点恢复。
    /// </summary>
    /// <param name="services">已迁移的检查服务。</param>
    /// <param name="http">真实 HTTP 客户端。</param>
    /// <param name="apiKey">示例应用 API Key。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunFlowPoliciesAsync(IServiceProvider services, HttpClient http, string apiKey,
        Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
        graph.Nodes.Single(node => node.Id == "n1").Assignee =
            new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "approver" };
        graph.Nodes.Single(node => node.Id == "n2").Assignee =
            new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "proxy" };
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var code in new[] { "LIMITED", "RETURN_NODE", "LEGACY" })
            {
                var versionId = code + "-version";
                db.Flows.Add(new FlowEntity
                {
                    Id = code + "-flow",
                    Code = code,
                    Name = code,
                    PublishedVersionId = versionId,
                });
                graph.WithdrawBeforeFirstDecision = code == "LIMITED";
                graph.ReturnMode = code == "RETURN_NODE" ? FlowReturnModes.ReturnNode : FlowReturnModes.Restart;
                var graphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options);
                if (code == "LEGACY")
                {
                    // 模拟策略属性加入前已发布的执行图，依靠契约缺省值恢复旧行为。
                    var legacy = JsonNode.Parse(graphJson)!.AsObject();
                    legacy.Remove("withdrawBeforeFirstDecision");
                    legacy.Remove("returnMode");
                    graphJson = legacy.ToJsonString(ApprovalJson.Options);
                }

                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = versionId,
                    FlowId = code + "-flow",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(new FlowDefinition { Code = code, Name = code },
                        ApprovalJson.Options),
                    GraphJson = graphJson,
                });
            }

            await db.SaveChangesAsync();
        }

        var limited = await StartPolicyFlowAsync(http, apiKey, "LIMITED", check);
        var limitedTask = limited.Tasks.Single();
        using (var approveRequest = ActionRequest(limitedTask.Id, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            Opinion = "已核准",
        }, apiKey, "limited-first-approve"))
        using (var approveResponse = await http.SendAsync(approveRequest))
        {
            check("限制撤回流程的首位审批人已处理且意见进入任务投影",
                approveResponse.StatusCode == HttpStatusCode.OK);
        }

        using (var withdrawRequest = CommandRequest(OpenApiRoutes.InstanceWithdraw.Replace("{id}", limited.Id),
            "alice", apiKey, "limited-withdraw"))
        using (var withdrawResponse = await http.SendAsync(withdrawRequest))
        {
            check("锁定流程限制首人处理后撤回", withdrawResponse.StatusCode == HttpStatusCode.Conflict);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var completed = await db.ApprovalTasks.AsNoTracking().SingleAsync(task => task.Id == limitedTask.Id);
            check("首人处理意见在后续任务建立后仍保留", completed.Opinion == "已核准");
        }

        var limitedSnapshot = await services.GetRequiredService<IApprovalRuntimeRepository>().LoadAsync(limited.Id)
            ?? throw new InvalidOperationException("受限流程状态未保存。");
        var limitedState = JsonSerializer.Deserialize<EngineState>(limitedSnapshot.Instance.StateJson,
            ApprovalJson.Options)!;
        var supplementTask = limitedState.Tasks.Single(task => task.NodeId == "n2" &&
            task.Status == EngineTaskStatuses.Pending);
        using (var supplementRequest = ActionRequest(supplementTask.Id, new OpenTaskActionRequest
        {
            User = "secretary",
            Action = OperationKinds.RequestSupplement,
            Opinion = "请补充材料",
        }, apiKey, "limited-supplement"))
        using (var supplementResponse = await http.SendAsync(supplementRequest))
        {
            check("要求补充的人工操作进入等待发起人状态", supplementResponse.StatusCode == HttpStatusCode.OK);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var body = await db.OutboxItems.AsNoTracking().Where(item => item.InstanceId == limited.Id &&
                item.Kind == "callback").Select(item => item.PayloadJson).ToArrayAsync();
            check("要求补充回调随审批事务固定等待状态与原事件正文", body.Any(item =>
                item.Contains("instance.supplementRequested", StringComparison.Ordinal) &&
                item.Contains("awaitingInitiator", StringComparison.Ordinal)));
        }

        var legacyInstance = await StartPolicyFlowAsync(http, apiKey, "LEGACY", check);
        using (var approveRequest = ActionRequest(legacyInstance.Tasks.Single().Id, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
        }, apiKey, "legacy-first-approve"))
        using (var approveResponse = await http.SendAsync(approveRequest))
        {
            check("旧发布图首位审批人可处理", approveResponse.StatusCode == HttpStatusCode.OK);
        }

        using (var withdrawRequest = CommandRequest(OpenApiRoutes.InstanceWithdraw.Replace("{id}", legacyInstance.Id),
            "alice", apiKey, "legacy-withdraw"))
        using (var withdrawResponse = await http.SendAsync(withdrawRequest))
        {
            check("旧发布图省略策略时仍可在结束前撤回", withdrawResponse.StatusCode == HttpStatusCode.OK);
        }

        var returnInstance = await StartPolicyFlowAsync(http, apiKey, "RETURN_NODE", check);
        using (var approveRequest = ActionRequest(returnInstance.Tasks.Single().Id, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
        }, apiKey, "return-node-first-approve"))
        using (var approveResponse = await http.SendAsync(approveRequest))
        {
            check("退回原节点流程先推进到第二个审批节点", approveResponse.StatusCode == HttpStatusCode.OK);
        }

        var repository = services.GetRequiredService<IApprovalRuntimeRepository>();
        var beforeReturn = await repository.LoadAsync(returnInstance.Id)
            ?? throw new InvalidOperationException("退回流程审批单未保存。");
        var beforeState = JsonSerializer.Deserialize<EngineState>(beforeReturn.Instance.StateJson, ApprovalJson.Options)!;
        var secondTask = beforeState.Tasks.Single(task => task.NodeId == "n2" && task.Status == EngineTaskStatuses.Pending);
        using (var returnRequest = ActionRequest(secondTask.Id, new OpenTaskActionRequest
        {
            User = "secretary",
            Action = OperationKinds.Return,
            Opinion = "请调整",
        }, apiKey, "return-node-to-initiator"))
        using (var returnResponse = await http.SendAsync(returnRequest))
        {
            check("第二节点可退回发起人并保存原节点重提策略",
                returnResponse.StatusCode == HttpStatusCode.OK);
        }

        var returned = await repository.LoadAsync(returnInstance.Id)
            ?? throw new InvalidOperationException("退回状态未保存。");
        var returnedState = JsonSerializer.Deserialize<EngineState>(returned.Instance.StateJson, ApprovalJson.Options)!;
        check("发起时策略随运行状态持久化", returnedState.Status == EngineInstanceStatuses.AwaitingInitiator &&
            returnedState.Operations.ReturnMode == FlowReturnModes.ReturnNode);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var body = await db.OutboxItems.AsNoTracking().Where(item => item.InstanceId == returnInstance.Id &&
                item.Kind == "callback").Select(item => item.PayloadJson).ToArrayAsync();
            check("退回发起人回调随审批事务固定等待状态与原事件正文", body.Any(item =>
                item.Contains("instance.returnedToInitiator", StringComparison.Ordinal) &&
                item.Contains("awaitingInitiator", StringComparison.Ordinal)));
        }
        var contexts = services.GetRequiredService<OpenStartContextBuilder>();
        var lockedGraph = JsonSerializer.Deserialize<FlowExecutionGraph>(returned.FlowVersion.GraphJson,
            ApprovalJson.Options)!;
        var lockedDefinition = JsonSerializer.Deserialize<FlowDefinition>(returned.FlowVersion.DefinitionJson,
            ApprovalJson.Options)!;
        var continuation = await contexts.BuildContinuationAsync(returned.Instance, lockedDefinition,
            lockedGraph, DateTimeOffset.UtcNow);
        var runtime = services.GetRequiredService<IApprovalRuntimeAppService>();
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = OperationKinds.Resubmit,
                InstanceId = returnInstance.Id,
                RequestId = "return-node-resubmit",
                Operation = new OperationInput { ActorId = "requester" },
            },
            Context = continuation,
        });
        var resumed = await repository.LoadAsync(returnInstance.Id)
            ?? throw new InvalidOperationException("重提状态未保存。");
        var resumedState = JsonSerializer.Deserialize<EngineState>(resumed.Instance.StateJson, ApprovalJson.Options)!;
        check("退回发起人后重提回到原第二节点而不从头审批",
            resumedState.Status == EngineInstanceStatuses.Running &&
            resumedState.Tasks.Any(task => task.NodeId == "n2" && task.Status == EngineTaskStatuses.Pending) &&
            resumedState.Tasks.Count(task => task.NodeId == "n1") == 1);
    }

    /// <summary>
    /// 以空业务数据发起策略场景审批单。
    /// </summary>
    /// <param name="http">真实 HTTP 客户端。</param>
    /// <param name="apiKey">示例应用 API Key。</param>
    /// <param name="code">已发布流程编码。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>发起后的审批单。</returns>
    private static async Task<OpenInstanceResponse> StartPolicyFlowAsync(HttpClient http, string apiKey, string code,
        Action<string, bool> check)
    {
        using var request = Request(new OpenStartRequest
        {
            FlowCode = code,
            BusinessKey = code + "-1",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { }),
        }, apiKey, "start-policy-" + code);
        using var response = await http.SendAsync(request);
        var instance = await response.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("策略流程 " + code + " 可以从本应用发起",
            response.StatusCode == HttpStatusCode.OK && instance is { Status: EngineInstanceStatuses.Running });
        return instance ?? throw new InvalidOperationException("策略流程发起响应为空。");
    }
}
