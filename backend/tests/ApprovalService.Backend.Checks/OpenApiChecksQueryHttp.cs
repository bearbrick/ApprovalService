using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 HTTP 宿主验证 API Key 与审批单来源应用的边界。
/// </summary>
internal static class OpenApiChecksQueryHttp
{
    /// <summary>
    /// 在两个接入应用之间验证详情和历史查询的隔离。
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
            var own = await clients.CreateAsync(new CreateClientAppInput { Code = "demo", Name = "示例", AllowedFlowCodes = ["PURCHASE"] });
            await clients.CreateAsync(new CreateClientAppInput { Code = "another", Name = "其他", AllowedFlowCodes = ["PURCHASE"] });
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
                await db.SaveChangesAsync();
            }

            var runtime = scope.ServiceProvider.GetRequiredService<IApprovalRuntimeAppService>();
            await runtime.ExecuteAsync(Start("own", "demo", "PO-1", graph));
            await runtime.ExecuteAsync(Start("other", "another", "PO-2", graph));

            var unauthenticated = await http.GetAsync("/api/v1/instances/own");
            check("审批单 HTTP 查询要求接入应用 API Key", unauthenticated.StatusCode == HttpStatusCode.Unauthorized);
            using var ownRequest = Request("/api/v1/instances/own", own.ApiKey);
            using var foreignRequest = Request("/api/v1/instances/other", own.ApiKey);
            using var historyRequest = Request("/api/v1/instances?flowCode=PURCHASE&businessKey=PO-1", own.ApiKey);
            using var foreignHistoryRequest = Request("/api/v1/instances?flowCode=PURCHASE&businessKey=PO-2", own.ApiKey);
            var ownResponse = await http.SendAsync(ownRequest);
            var foreignResponse = await http.SendAsync(foreignRequest);
            var historyResponse = await http.SendAsync(historyRequest);
            var foreignHistoryResponse = await http.SendAsync(foreignHistoryRequest);
            var detail = JsonSerializer.Deserialize<OpenInstanceResponse>(await ownResponse.Content.ReadAsStringAsync(), ApprovalJson.Options);
            var history = JsonSerializer.Deserialize<OpenInstanceResponse[]>(await historyResponse.Content.ReadAsStringAsync(), ApprovalJson.Options);
            var foreignHistory = JsonSerializer.Deserialize<OpenInstanceResponse[]>(await foreignHistoryResponse.Content.ReadAsStringAsync(), ApprovalJson.Options);
            check("本应用 API Key 可查询审批单详情与业务单号历史", ownResponse.StatusCode == HttpStatusCode.OK &&
                detail is { Id: "own", ClientAppCode: "demo" } && detail.Tasks.Count == 1 &&
                historyResponse.StatusCode == HttpStatusCode.OK && history is [{ Id: "own" }]);
            check("其他应用的审批单详情和历史均不可通过本应用 Key 读取", foreignResponse.StatusCode == HttpStatusCode.Forbidden &&
                foreignHistoryResponse.StatusCode == HttpStatusCode.OK && foreignHistory is { Length: 0 });
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 给一条查询请求附加原始 API Key。
    /// </summary>
    /// <param name="path">接口地址。</param>
    /// <param name="apiKey">新签发的 API Key。</param>
    /// <returns>可发送的请求。</returns>
    private static HttpRequestMessage Request(string path, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        return request;
    }

    /// <summary>
    /// 构造保留接入应用来源的发起命令。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="appCode">来源接入应用。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="graph">执行图。</param>
    /// <returns>运行命令。</returns>
    private static RuntimeCommandInput Start(string instanceId, string appCode, string businessKey,
        FlowExecutionGraph graph) => new()
        {
            Command = new EngineCommand { Kind = EngineCommandKinds.Start, RequestId = instanceId, InstanceId = instanceId },
            Context = EngineScenarioChecks.Context(graph),
            StartInstance = new InstanceEntity
            {
                Id = instanceId,
                SerialNo = "AP-" + instanceId,
                FlowVersionId = "version",
                FlowCode = "PURCHASE",
                BusinessKey = businessKey,
                Title = "采购",
                InitiatorId = "requester",
                ClientAppCode = appCode,
                ApprovalDataJson = "{}",
            },
        };
}
