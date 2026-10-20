using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 以真实 HTTP、组织绑定和迁移数据库检查开放发起的权限、公式与在途幂等。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 执行两个接入应用的发起场景。
    /// </summary>
    /// <param name="backendRoot">后端项目根目录。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            };
            await using var server = new ServerFactory(backendRoot, directory, settings);
            using var http = server.CreateClient();
            await http.GetAsync(ApiRoutes.HealthReady);
            using var scope = server.Services.CreateScope();
            var clients = scope.ServiceProvider.GetRequiredService<ClientAppAppService>();
            var demo = await clients.CreateAsync(new CreateClientAppInput
            {
                Code = "demo",
                Name = "示例",
                AllowedFlowCodes = ["PURCHASE", "MULTI", "SCOPE", "SELECTED", "DYNAMIC", "INPUT", "LIMITED", "RETURN_NODE", "LEGACY"],
            });
            var other = await clients.CreateAsync(new CreateClientAppInput
            {
                Code = "other",
                Name = "其他",
                AllowedFlowCodes = ["PURCHASE"],
            });
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            graph.Nodes.Single(node => node.Id == "n1").Assignee = new FlowAssignee
            {
                Source = FlowAssigneeSources.Member,
                Value = "approver",
            };
            var definition = new FlowDefinition
            {
                Code = "PURCHASE",
                Name = "采购",
                Data = new FlowDataDefinition
                {
                    Fields =
                    [
                        new FlowFieldDefinition { Key = "amount", Name = "金额", Type = FlowFieldTypes.Money, Required = true },
                        new FlowFieldDefinition { Key = "doubleAmount", Name = "双倍金额", Type = FlowFieldTypes.Money,
                            Formula = "${amount} * 2" },
                    ],
                },
            };
            var multiDefinition = new FlowDefinition
            {
                Code = "MULTI",
                Name = "多部门申请",
                AllowedInitiatorDepartments = ["D2"],
                Data = definition.Data,
            };
            var scopeDefinition = new FlowDefinition
            {
                Code = "SCOPE",
                Name = "限定部门申请",
                AllowedInitiatorDepartments = ["D1"],
                Data = definition.Data,
            };
            await using (var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>())
            {
                db.Flows.Add(new FlowEntity { Id = "flow", Code = "PURCHASE", Name = "采购", PublishedVersionId = "version" });
                db.Flows.Add(new FlowEntity { Id = "multi", Code = "MULTI", Name = "多部门申请", PublishedVersionId = "multi-version" });
                db.Flows.Add(new FlowEntity { Id = "scope", Code = "SCOPE", Name = "限定部门申请", PublishedVersionId = "scope-version" });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "version",
                    FlowId = "flow",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "multi-version",
                    FlowId = "multi",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(multiDefinition, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "scope-version",
                    FlowId = "scope",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(scopeDefinition, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                db.People.AddRange(
                    new PersonEntity { Id = "requester", EmployeeNumber = "E001", Name = "发起人" },
                    new PersonEntity { Id = "approver", EmployeeNumber = "E002", Name = "审批人" },
                    new PersonEntity { Id = "proxy", EmployeeNumber = "E003", Name = "代发人" });
                db.Departments.AddRange(
                    new DepartmentEntity { Id = "department-1", Code = "D1", Name = "一部" },
                    new DepartmentEntity { Id = "department-2", Code = "D2", Name = "二部" },
                    new DepartmentEntity { Id = "department-3", Code = "D3", Name = "三部" });
                db.PersonDepartments.AddRange(
                    new PersonDepartmentEntity { PersonId = "requester", DepartmentId = "department-1", IsPrimary = true },
                    new PersonDepartmentEntity { PersonId = "requester", DepartmentId = "department-2" });
                db.ExternalPersonBindings.Add(new ExternalPersonBindingEntity
                {
                    ClientAppCode = "demo",
                    ExternalUserId = "alice",
                    PersonId = "requester",
                    Method = "manual",
                });
                db.ExternalPersonBindings.Add(new ExternalPersonBindingEntity
                {
                    ClientAppCode = "demo",
                    ExternalUserId = "secretary",
                    PersonId = "proxy",
                    Method = "manual",
                });
                db.ExternalPersonBindings.Add(new ExternalPersonBindingEntity
                {
                    ClientAppCode = "demo",
                    ExternalUserId = "bob",
                    PersonId = "approver",
                    Method = "manual",
                });
                db.ExternalDepartmentBindings.AddRange(
                    new ExternalDepartmentBindingEntity
                    {
                        ClientAppCode = "demo",
                        ExternalDepartmentId = "dept-two",
                        DepartmentId = "department-2",
                        Method = "manual",
                    },
                    new ExternalDepartmentBindingEntity
                    {
                        ClientAppCode = "demo",
                        ExternalDepartmentId = "dept-three",
                        DepartmentId = "department-3",
                        Method = "manual",
                    },
                    new ExternalDepartmentBindingEntity
                    {
                        ClientAppCode = "demo",
                        ExternalDepartmentId = "D1",
                        DepartmentId = "department-2",
                        Method = "manual",
                    });
                await db.SaveChangesAsync();
            }

            var start = new OpenStartRequest
            {
                FlowCode = "PURCHASE",
                BusinessKey = "PO-1",
                Title = "采购单",
                Initiator = "alice",
                Data = JsonSerializer.SerializeToElement(new { amount = 12, doubleAmount = 999 }),
            };
            using var firstRequest = Request(start, demo.ApiKey, "start-1");
            using var firstResponse = await http.SendAsync(firstRequest);
            var first = JsonSerializer.Deserialize<OpenInstanceResponse>(await firstResponse.Content.ReadAsStringAsync(),
                ApprovalJson.Options);
            check("开放发起按已绑定人员建立审批任务并服务端重算公式", firstResponse.StatusCode == HttpStatusCode.OK &&
                first is { Status: EngineInstanceStatuses.Running, InitiatorId: "requester", ClientAppCode: "demo" } &&
                first.Tasks is [{ AssigneeId: "approver" }] && first.Data.GetProperty("doubleAmount").GetDecimal() == 24);

            using var repeatRequest = Request(start, demo.ApiKey, "start-2");
            using var repeatResponse = await http.SendAsync(repeatRequest);
            var repeat = JsonSerializer.Deserialize<OpenInstanceResponse>(await repeatResponse.Content.ReadAsStringAsync(),
                ApprovalJson.Options);
            check("同应用同流程同业务单号在途重复发起返回原审批单", repeatResponse.StatusCode == HttpStatusCode.OK &&
                repeat?.Id == first?.Id && repeat?.SerialNo == first?.SerialNo);

            using var foreignRequest = Request(start, other.ApiKey, "start-foreign");
            using var foreignResponse = await http.SendAsync(foreignRequest);
            check("另一应用同号发起只返回冲突而不泄露原审批单", foreignResponse.StatusCode == HttpStatusCode.Conflict);

            using var unboundRequest = Request(new OpenStartRequest
            {
                FlowCode = "PURCHASE",
                BusinessKey = "PO-2",
                Initiator = "E001",
                Data = JsonSerializer.SerializeToElement(new { amount = 2 }),
            }, demo.ApiKey, "start-unbound");
            using var unboundResponse = await http.SendAsync(unboundRequest);
            check("全局工号不能绕过本应用人员绑定发起", unboundResponse.StatusCode == HttpStatusCode.BadRequest);

            using var missingDataRequest = Request(new OpenStartRequest
            {
                FlowCode = "PURCHASE",
                BusinessKey = "PO-3",
                Initiator = "alice",
            }, demo.ApiKey, "start-missing-data");
            using var missingDataResponse = await http.SendAsync(missingDataRequest);
            check("取数钩子协议未确认时只传单号发起给出可纠正错误",
                missingDataResponse.StatusCode == HttpStatusCode.BadRequest);

            using var secondRequest = Request(new OpenStartRequest
            {
                FlowCode = "PURCHASE",
                BusinessKey = "PO-4",
                Initiator = "alice",
                Data = JsonSerializer.SerializeToElement(new { amount = 7 }),
            }, demo.ApiKey, "start-4");
            using var secondResponse = await http.SendAsync(secondRequest);
            var second = JsonSerializer.Deserialize<OpenInstanceResponse>(await secondResponse.Content.ReadAsStringAsync(),
                ApprovalJson.Options);
            check("不同业务单号分配不同且递增的审批编号", secondResponse.StatusCode == HttpStatusCode.OK &&
                first is not null && second is not null && first.SerialNo.StartsWith("SP-") &&
                first.SerialNo.EndsWith("-000001") && second.SerialNo.EndsWith("-000002"));

            using var wrongActorRequest = ActionRequest(second!.Tasks.Single().Id,
                new OpenTaskActionRequest { User = "alice", Action = EngineCommandKinds.Approve }, demo.ApiKey,
                "action-wrong-actor");
            using var wrongActorResponse = await http.SendAsync(wrongActorRequest);
            check("非任务处理人不能借本应用 Key 同意", wrongActorResponse.StatusCode == HttpStatusCode.Forbidden);

            using var foreignActionRequest = ActionRequest(second.Tasks.Single().Id,
                new OpenTaskActionRequest { User = "bob", Action = EngineCommandKinds.Approve }, other.ApiKey,
                "action-foreign");
            using var foreignActionResponse = await http.SendAsync(foreignActionRequest);
            check("另一接入应用不能审批本应用任务", foreignActionResponse.StatusCode == HttpStatusCode.Forbidden);

            using var approveRequest = ActionRequest(second.Tasks.Single().Id,
                new OpenTaskActionRequest { User = "bob", Action = EngineCommandKinds.Approve }, demo.ApiKey,
                "action-approve");
            using var approveResponse = await http.SendAsync(approveRequest);
            var approved = JsonSerializer.Deserialize<OpenInstanceResponse>(await approveResponse.Content.ReadAsStringAsync(),
                ApprovalJson.Options);
            check("已绑定审批人经统一引擎入口同意并完成审批单", approveResponse.StatusCode == HttpStatusCode.OK &&
                approved is { Status: EngineInstanceStatuses.Approved });

            using var multiRequest = Request(new OpenStartRequest
            {
                FlowCode = "MULTI",
                BusinessKey = "MULTI-1",
                Initiator = "alice",
                SubmittedBy = "secretary",
                InitiatingDepartment = "dept-two",
                Data = JsonSerializer.SerializeToElement(new { amount = 3 }),
            }, demo.ApiKey, "start-multi");
            using var multiResponse = await http.SendAsync(multiRequest);
            var multi = JsonSerializer.Deserialize<OpenInstanceResponse>(await multiResponse.Content.ReadAsStringAsync(),
                ApprovalJson.Options);
            check("多部门发起选择已绑定所属部门且记录本应用已绑定代发人", multiResponse.StatusCode == HttpStatusCode.OK &&
                multi is { InitiatorDepartmentId: "department-2", SubmittedById: "proxy" });
            await RunDataUpdateStorageAsync(scope.ServiceProvider, multi!, multiDefinition, graph, check);

            using var spoofedScopeRequest = Request(new OpenStartRequest
            {
                FlowCode = "SCOPE",
                BusinessKey = "SCOPE-1",
                Initiator = "alice",
                InitiatingDepartment = "D1",
                Data = JsonSerializer.SerializeToElement(new { amount = 3 }),
            }, demo.ApiKey, "start-scope");
            using var spoofedScopeResponse = await http.SendAsync(spoofedScopeRequest);
            check("应用伪造同名部门绑定不能绕过管理员的流程发起部门范围",
                spoofedScopeResponse.StatusCode == HttpStatusCode.Forbidden);

            using var unownedDepartmentRequest = Request(new OpenStartRequest
            {
                FlowCode = "MULTI",
                BusinessKey = "MULTI-2",
                Initiator = "alice",
                InitiatingDepartment = "dept-three",
                Data = JsonSerializer.SerializeToElement(new { amount = 3 }),
            }, demo.ApiKey, "start-unowned-department");
            using var unownedDepartmentResponse = await http.SendAsync(unownedDepartmentRequest);
            check("发起部门虽已绑定但发起人不属于该部门时拒绝",
                unownedDepartmentResponse.StatusCode == HttpStatusCode.Forbidden);

            using var unboundProxyRequest = Request(new OpenStartRequest
            {
                FlowCode = "MULTI",
                BusinessKey = "MULTI-3",
                Initiator = "alice",
                SubmittedBy = "E003",
                InitiatingDepartment = "dept-two",
                Data = JsonSerializer.SerializeToElement(new { amount = 3 }),
            }, demo.ApiKey, "start-unbound-proxy");
            using var unboundProxyResponse = await http.SendAsync(unboundProxyRequest);
            check("代发人不能绕过本应用既有绑定", unboundProxyResponse.StatusCode == HttpStatusCode.BadRequest);

            using var foreignRemindRequest = CommandRequest(OpenApiRoutes.InstanceRemind.Replace("{id}", first!.Id),
                "alice", other.ApiKey, "remind-foreign");
            using var foreignRemindResponse = await http.SendAsync(foreignRemindRequest);
            check("另一应用不能催办本应用审批单", foreignRemindResponse.StatusCode == HttpStatusCode.Forbidden);

            using var unboundRemindRequest = CommandRequest(OpenApiRoutes.InstanceRemind.Replace("{id}", first.Id),
                "E001", demo.ApiKey, "remind-unbound");
            using var unboundRemindResponse = await http.SendAsync(unboundRemindRequest);
            check("催办不能以未绑定全局工号冒用发起人", unboundRemindResponse.StatusCode == HttpStatusCode.Forbidden);

            using var remindRequest = CommandRequest(OpenApiRoutes.InstanceRemind.Replace("{id}", first.Id),
                "alice", demo.ApiKey, "remind-one");
            using var remindResponse = await http.SendAsync(remindRequest);
            check("发起人可催办当前任务并留下事件", remindResponse.StatusCode == HttpStatusCode.OK &&
                (await remindResponse.Content.ReadAsStringAsync()).Contains("reminderRequested", StringComparison.Ordinal));

            using var earlyRemindRequest = CommandRequest(OpenApiRoutes.InstanceRemind.Replace("{id}", first.Id),
                "alice", demo.ApiKey, "remind-two");
            using var earlyRemindResponse = await http.SendAsync(earlyRemindRequest);
            check("同一步一小时内不能重复催办", earlyRemindResponse.StatusCode == HttpStatusCode.Conflict);

            using var withdrawRequest = CommandRequest(OpenApiRoutes.InstanceWithdraw.Replace("{id}", first.Id),
                "alice", demo.ApiKey, "withdraw-one");
            using var withdrawResponse = await http.SendAsync(withdrawRequest);
            var withdrawn = JsonSerializer.Deserialize<OpenInstanceResponse>(await withdrawResponse.Content.ReadAsStringAsync(),
                ApprovalJson.Options);
            check("发起人可撤回在途单且任务被取消", withdrawResponse.StatusCode == HttpStatusCode.OK &&
                withdrawn is { Status: EngineInstanceStatuses.Withdrawn } &&
                withdrawn.Tasks.All(task => task.Status == EngineTaskStatuses.Cancelled));

            await RunRoutingAsync(scope.ServiceProvider, http, demo.ApiKey, check);
            await RunApproverInputsAsync(scope.ServiceProvider, http, demo.ApiKey, check);
            await RunFlowPoliciesAsync(scope.ServiceProvider, http, demo.ApiKey, check);
            await RunPerformanceAsync(http, demo.ApiKey, check);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 构造带 API Key 与稳定请求 ID 的 JSON 发起请求。
    /// </summary>
    /// <param name="input">发起数据。</param>
    /// <param name="apiKey">接入应用 Key。</param>
    /// <param name="requestId">唯一请求 ID。</param>
    /// <returns>HTTP 请求。</returns>
    private static HttpRequestMessage Request(OpenStartRequest input, string apiKey, string requestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OpenApiRoutes.Instances)
        {
            Content = JsonContent.Create(input),
        };
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        request.Headers.Add(HeaderNames.RequestId, requestId);
        return request;
    }

    /// <summary>
    /// 构造实例级操作请求，身份仍由服务端按 API Key 的应用绑定核对。
    /// </summary>
    /// <param name="route">目标路径。</param>
    /// <param name="user">外部人员 ID。</param>
    /// <param name="apiKey">接入应用 Key。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <returns>HTTP 请求。</returns>
    private static HttpRequestMessage CommandRequest(string route, string user, string apiKey, string requestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(new OpenInstanceCommandRequest { User = user }),
        };
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        request.Headers.Add(HeaderNames.RequestId, requestId);
        return request;
    }

    /// <summary>
    /// 构造人工任务操作请求。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="input">操作参数。</param>
    /// <param name="apiKey">接入应用 Key。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <returns>HTTP 请求。</returns>
    private static HttpRequestMessage ActionRequest(string taskId, OpenTaskActionRequest input, string apiKey,
        string requestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            OpenApiRoutes.TaskActions.Replace("{id}", Uri.EscapeDataString(taskId)))
        {
            Content = JsonContent.Create(input),
        };
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        request.Headers.Add(HeaderNames.RequestId, requestId);
        return request;
    }
}
