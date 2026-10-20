using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 HTTP 宿主验证开放待办只使用本应用绑定，并受跨应用授权限制。
/// </summary>
internal static class OpenApiChecksTasks
{
    /// <summary>
    /// 建立两应用同一处理人的待办并检查默认与跨应用范围。
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
            var apps = scope.ServiceProvider.GetRequiredService<ClientAppAppService>();
            var demo = await apps.CreateAsync(new CreateClientAppInput
            {
                Code = "demo",
                Name = "示例",
                AllowedFlowCodes = ["PURCHASE"],
                CanViewAllTasks = true,
            });
            var other = await apps.CreateAsync(new CreateClientAppInput
            {
                Code = "other",
                Name = "其他",
                AllowedFlowCodes = ["PURCHASE"],
            });
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            await using (var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>())
            {
                db.Flows.Add(new FlowEntity { Id = "flow", Code = "PURCHASE", Name = "采购", PublishedVersionId = "version" });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "version",
                    FlowId = "flow",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(new FlowDefinition(), ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                db.People.Add(new PersonEntity { Id = "approver", EmployeeNumber = "E001", Name = "处理人" });
                db.ExternalPersonBindings.AddRange(
                    new ExternalPersonBindingEntity
                    {
                        ClientAppCode = "demo",
                        ExternalUserId = "alice",
                        PersonId = "approver",
                        Method = "manual",
                    },
                    new ExternalPersonBindingEntity
                    {
                        ClientAppCode = "other",
                        ExternalUserId = "other-alice",
                        PersonId = "approver",
                        Method = "manual",
                    });
                await db.SaveChangesAsync();
            }

            var runtime = scope.ServiceProvider.GetRequiredService<IApprovalRuntimeAppService>();
            await runtime.ExecuteAsync(Start("demo-instance", "demo", graph));
            await runtime.ExecuteAsync(Start("other-instance", "other", graph));

            using var ownCountRequest = Request("/api/v1/tasks/count?user=alice", demo.ApiKey);
            using var allCountRequest = Request("/api/v1/tasks/count?user=alice&scope=all", demo.ApiKey);
            using var foreignCountRequest = Request("/api/v1/tasks/count?user=E001", demo.ApiKey);
            using var forbiddenCountRequest = Request("/api/v1/tasks/count?user=other-alice&scope=all", other.ApiKey);
            using var listRequest = Request("/api/v1/tasks?user=alice&take=10", demo.ApiKey);
            using var invalidPageRequest = Request("/api/v1/tasks?user=alice&skip=-1", demo.ApiKey);
            using var ownCount = await http.SendAsync(ownCountRequest);
            using var allCount = await http.SendAsync(allCountRequest);
            using var foreignCount = await http.SendAsync(foreignCountRequest);
            using var forbiddenCount = await http.SendAsync(forbiddenCountRequest);
            using var list = await http.SendAsync(listRequest);
            using var invalidPage = await http.SendAsync(invalidPageRequest);
            var own = JsonSerializer.Deserialize<OpenTaskCountResponse>(await ownCount.Content.ReadAsStringAsync(), ApprovalJson.Options);
            var all = JsonSerializer.Deserialize<OpenTaskCountResponse>(await allCount.Content.ReadAsStringAsync(), ApprovalJson.Options);
            var tasks = JsonSerializer.Deserialize<OpenTaskResponse[]>(await list.Content.ReadAsStringAsync(), ApprovalJson.Options);
            check("开放待办默认只统计本应用，获授权时可统计全部应用", ownCount.StatusCode == HttpStatusCode.OK &&
                own?.Pending == 1 && allCount.StatusCode == HttpStatusCode.OK && all?.Pending == 2);
            check("开放待办列表只返回本应用绑定人员的本应用任务", list.StatusCode == HttpStatusCode.OK &&
                tasks is [{ InstanceId: "demo-instance", BusinessKey: "PO-demo" }]);
            check("全局工号不能绕过本应用绑定查待办", foreignCount.StatusCode == HttpStatusCode.NotFound);
            check("没有跨应用授权不能读取全部待办", forbiddenCount.StatusCode == HttpStatusCode.Forbidden);
            check("无效分页返回可纠正错误", invalidPage.StatusCode == HttpStatusCode.BadRequest);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 构造带 API Key 的查询请求。
    /// </summary>
    /// <param name="path">请求路径。</param>
    /// <param name="apiKey">接入应用密钥。</param>
    /// <returns>HTTP 请求。</returns>
    private static HttpRequestMessage Request(string path, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        return request;
    }

    /// <summary>
    /// 直接通过统一引擎入口建立测试待办。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="appCode">来源应用。</param>
    /// <param name="graph">执行图。</param>
    /// <returns>运行命令。</returns>
    private static RuntimeCommandInput Start(string instanceId, string appCode, FlowExecutionGraph graph)
    {
        var context = EngineScenarioChecks.Context(graph);
        context.Assignees = graph.Nodes.Where(node => node.Type == FlowNodeTypes.Approval)
            .ToDictionary(node => node.Id, _ => "approver", StringComparer.Ordinal);
        return new RuntimeCommandInput
        {
            Command = new EngineCommand { Kind = EngineCommandKinds.Start, RequestId = instanceId, InstanceId = instanceId },
            Context = context,
            StartInstance = new InstanceEntity
            {
                Id = instanceId,
                SerialNo = "AP-" + instanceId,
                FlowVersionId = "version",
                FlowCode = "PURCHASE",
                BusinessKey = "PO-" + appCode,
                Title = "采购",
                InitiatorId = "requester",
                ClientAppCode = appCode,
                ApprovalDataJson = "{}",
            },
        };
    }
}
