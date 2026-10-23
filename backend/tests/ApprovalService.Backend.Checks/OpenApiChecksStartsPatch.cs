using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 以真实 HTTP 和数据库验证审批中部分更新的字段授权、公式与审计。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 发起含顶层和明细字段的审批单，验证 PATCH 的成功及拒绝路径。
    /// </summary>
    /// <param name="services">已初始化的服务范围。</param>
    /// <param name="http">进程内真实 HTTP 客户端。</param>
    /// <param name="apiKey">审批单所属应用 Key。</param>
    /// <param name="foreignKey">其他应用 Key。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunPatchAsync(IServiceProvider services, HttpClient http, string apiKey,
        string foreignKey, Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        graph.Nodes.Single(node => node.Id == "n1").Assignee = new FlowAssignee
        {
            Source = FlowAssigneeSources.Member,
            Value = "approver",
        };
        graph.Edges.RemoveAll(edge => edge.SourceId == "n1" && edge.TargetId == "end");
        graph.Nodes.AddRange(
        [
            new FlowExecutionNode { Id = "split", Type = FlowNodeTypes.ExclusiveGateway,
                GatewayDirection = FlowGatewayDirections.Split, GatewayPairId = "patch-pair" },
            new FlowExecutionNode { Id = "high", Type = FlowNodeTypes.Approval,
                Assignee = new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "requester" },
                Approval = new FlowApprovalSettings { SelfApprovalBehavior = FlowSelfApprovalBehaviors.Self } },
            new FlowExecutionNode { Id = "low", Type = FlowNodeTypes.Approval,
                Assignee = new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "requester" },
                Approval = new FlowApprovalSettings { SelfApprovalBehavior = FlowSelfApprovalBehaviors.Self } },
            new FlowExecutionNode { Id = "join", Type = FlowNodeTypes.ExclusiveGateway,
                GatewayDirection = FlowGatewayDirections.Join, GatewayPairId = "patch-pair" },
        ]);
        graph.Edges.AddRange(
        [
            new FlowExecutionEdge { Id = "to-split", SourceId = "n1", TargetId = "split" },
            new FlowExecutionEdge { Id = "high-route", SourceId = "split", TargetId = "high",
                Condition = "${amount} >= 5", Priority = 0 },
            new FlowExecutionEdge { Id = "low-route", SourceId = "split", TargetId = "low",
                IsDefault = true, Priority = 1 },
            new FlowExecutionEdge { Id = "high-join", SourceId = "high", TargetId = "join" },
            new FlowExecutionEdge { Id = "low-join", SourceId = "low", TargetId = "join" },
            new FlowExecutionEdge { Id = "join-end", SourceId = "join", TargetId = "end" },
        ]);
        var definition = new FlowDefinition
        {
            Code = "PATCH",
            Name = "数据更新",
            Data = new FlowDataDefinition
            {
                Fields =
                [
                    new FlowFieldDefinition
                    {
                        Key = "amount", Name = "金额", Type = FlowFieldTypes.Money,
                        Required = true, BusinessUpdateAllowed = true,
                    },
                    new FlowFieldDefinition
                    {
                        Key = "doubleAmount", Name = "双倍金额", Type = FlowFieldTypes.Money,
                        Formula = "${amount} * 2",
                    },
                    new FlowFieldDefinition { Key = "locked", Name = "锁定字段", Type = FlowFieldTypes.Text },
                    new FlowFieldDefinition
                    {
                        Key = "lines", Name = "明细", Type = FlowFieldTypes.DetailTable,
                        Children =
                        [
                            new FlowFieldDefinition
                            {
                                Key = "qty", Name = "数量", Type = FlowFieldTypes.Number,
                                BusinessUpdateAllowed = true,
                            },
                            new FlowFieldDefinition { Key = "code", Name = "编码", Type = FlowFieldTypes.Text },
                        ],
                    },
                ],
            },
        };
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Flows.Add(new FlowEntity
            {
                Id = "patch-flow",
                Code = "PATCH",
                Name = "数据更新",
                PublishedVersionId = "patch-version",
            });
            db.FlowVersions.Add(new FlowVersionEntity
            {
                Id = "patch-version",
                FlowId = "patch-flow",
                Version = 1,
                SchemaVersion = 1,
                DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
                GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
            });
            await db.SaveChangesAsync();
        }

        using var startRequest = Request(new OpenStartRequest
        {
            FlowCode = "PATCH",
            BusinessKey = "PATCH-1",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new
            {
                amount = 2,
                locked = "original",
                lines = new[] { new { qty = 1, code = "a" }, new { qty = 2, code = "b" } },
            }),
        }, apiKey, "patch-start");
        using var startResponse = await http.SendAsync(startRequest);
        var started = JsonSerializer.Deserialize<OpenInstanceResponse>(await startResponse.Content.ReadAsStringAsync(),
            ApprovalJson.Options);
        check("审批中更新场景先按锁定发布版本发起待办", startResponse.StatusCode == HttpStatusCode.OK &&
            started is { Status: EngineInstanceStatuses.Running, Tasks.Count: 1 });
        var route = OpenApiRoutes.InstanceData.Replace("{id}", started!.Id);

        using var foreignRequest = PatchRequest(route, foreignKey, "alice", "patch-foreign", new
        {
            amount = 3
        });
        using var foreignResponse = await http.SendAsync(foreignRequest);
        check("其他应用不能更新同一业务单号的审批数据", foreignResponse.StatusCode == HttpStatusCode.Forbidden);

        using var unboundRequest = PatchRequest(route, apiKey, "E001", "patch-unbound", new
        {
            amount = 3
        });
        using var unboundResponse = await http.SendAsync(unboundRequest);
        check("未绑定工号不能代表实际操作人更新审批数据", unboundResponse.StatusCode == HttpStatusCode.Forbidden);

        using var lockedRequest = PatchRequest(route, apiKey, "alice", "patch-locked", new
        {
            locked = "changed"
        });
        using var lockedResponse = await http.SendAsync(lockedRequest);
        using var formulaRequest = PatchRequest(route, apiKey, "alice", "patch-formula", new
        {
            doubleAmount = 99
        });
        using var formulaResponse = await http.SendAsync(formulaRequest);
        using var hiddenChildRequest = PatchRequest(route, apiKey, "alice", "patch-child-locked",
            new
            {
                lines = new object[] { new { code = "x" }, new { } }
            });
        using var hiddenChildResponse = await http.SendAsync(hiddenChildRequest);
        check("未开启字段、公式和未开放明细子字段不能经 PATCH 修改",
            lockedResponse.StatusCode == HttpStatusCode.BadRequest &&
            formulaResponse.StatusCode == HttpStatusCode.BadRequest &&
            hiddenChildResponse.StatusCode == HttpStatusCode.BadRequest);

        using var invalidTypeRequest = PatchRequest(route, apiKey, "alice", "patch-invalid", new
        {
            amount = "bad"
        });
        using var invalidTypeResponse = await http.SendAsync(invalidTypeRequest);
        check("获授权字段仍受字段类型校验", invalidTypeResponse.StatusCode == HttpStatusCode.BadRequest);

        using var validRequest = PatchRequest(route, apiKey, "alice", "patch-valid",
            new
            {
                amount = 5,
                lines = new object[] { new { qty = 4 }, new { } }
            });
        using var validResponse = await http.SendAsync(validRequest);
        var updated = JsonSerializer.Deserialize<OpenInstanceResponse>(await validResponse.Content.ReadAsStringAsync(),
            ApprovalJson.Options);
        var eventDetail = updated?.Events.SingleOrDefault(item => item.Event == "businessDataUpdated")?.Detail;
        check("PATCH 只改开放字段和明细行，服务端重算公式并留下改前改后审计",
            validResponse.StatusCode == HttpStatusCode.OK && updated is not null &&
            updated.Data.GetProperty("amount").GetDecimal() == 5 &&
            updated.Data.GetProperty("doubleAmount").GetDecimal() == 10 &&
            updated.Data.GetProperty("locked").GetString() == "original" &&
            updated.Data.GetProperty("lines")[0].GetProperty("qty").GetDecimal() == 4 &&
            updated.Data.GetProperty("lines")[0].GetProperty("code").GetString() == "a" &&
            eventDetail?.GetProperty("audit").GetProperty("dataChanges").GetProperty("amount")
                .GetProperty("before").GetDecimal() == 2 &&
            eventDetail?.GetProperty("audit").GetProperty("dataChanges").GetProperty("amount")
                .GetProperty("after").GetDecimal() == 5 &&
            updated.Tasks.Single().Id == started.Tasks.Single().Id);

        using var replayRequest = PatchRequest(route, apiKey, "alice", "patch-valid", new
        {
            amount = 5
        });
        using var replayResponse = await http.SendAsync(replayRequest);
        var replayed = JsonSerializer.Deserialize<OpenInstanceResponse>(await replayResponse.Content.ReadAsStringAsync(),
            ApprovalJson.Options);
        check("PATCH 同请求 ID 重试不追加第二条数据更新事件",
            replayResponse.StatusCode == HttpStatusCode.OK && replayed?.Events.Count(item => item.Event ==
                "businessDataUpdated") == 1);

        using var advanceRequest = ActionRequest(updated!.Tasks.Single().Id,
            new OpenTaskActionRequest { User = "bob", Action = EngineCommandKinds.Approve }, apiKey,
            "patch-advance");
        using var advanceResponse = await http.SendAsync(advanceRequest);
        var advanced = JsonSerializer.Deserialize<OpenInstanceResponse>(await advanceResponse.Content.ReadAsStringAsync(),
            ApprovalJson.Options);
        check("PATCH 不静默改派已到达任务，后续网关按更新后的金额重新选路",
            advanceResponse.StatusCode == HttpStatusCode.OK && advanced is not null &&
            advanced.Tasks.Any(task => task.NodeId == "high" && task.Status == EngineTaskStatuses.Pending) &&
            !advanced.Tasks.Any(task => task.NodeId == "low"));
    }

    /// <summary>
    /// 构造部分数据更新请求。
    /// </summary>
    /// <param name="route">审批单数据路由。</param>
    /// <param name="apiKey">接入应用 Key。</param>
    /// <param name="user">实际操作人的本应用外部 ID。</param>
    /// <param name="requestId">幂等请求 ID。</param>
    /// <param name="data">部分字段数据。</param>
    /// <returns>HTTP 请求。</returns>
    private static HttpRequestMessage PatchRequest(string route, string apiKey, string user, string requestId,
        object data)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, route)
        {
            Content = JsonContent.Create(new OpenDataPatchRequest
            {
                User = user,
                Data = JsonSerializer.SerializeToElement(data),
            }),
        };
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        request.Headers.Add(HeaderNames.RequestId, requestId);
        return request;
    }
}
