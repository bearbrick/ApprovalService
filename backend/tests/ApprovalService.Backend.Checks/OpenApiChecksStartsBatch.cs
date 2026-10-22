using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 批量发起逐单提交和重试的真实 HTTP 场景。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 校验单项失败不回滚前后成功项，重复调用返回原在途审批单。
    /// </summary>
    /// <param name="services">已迁移的检查服务。</param>
    /// <param name="http">真实 HTTP 客户端。</param>
    /// <param name="apiKey">已授权应用的 API Key。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunBatchAsync(IServiceProvider services, HttpClient http, string apiKey,
        Action<string, bool> check)
    {
        var batch = new OpenBatchStartRequest
        {
            Items =
            [
                new OpenStartRequest
                {
                    FlowCode = "PURCHASE", BusinessKey = "BATCH-1", Initiator = "alice",
                    Data = JsonSerializer.SerializeToElement(new { amount = 10 }),
                },
                new OpenStartRequest
                {
                    FlowCode = "PURCHASE", BusinessKey = "BATCH-2", Initiator = "alice",
                    Data = JsonSerializer.SerializeToElement(new { amount = "金额错误" }),
                },
                null,
                new OpenStartRequest
                {
                    FlowCode = "PURCHASE", BusinessKey = "BATCH-3", Initiator = "alice",
                    Data = JsonSerializer.SerializeToElement(new { amount = 30 }),
                },
            ],
        };
        using var request = BatchRequest(batch, apiKey, "batch-start");
        using var response = await http.SendAsync(request);
        var result = await response.Content.ReadFromJsonAsync<OpenBatchStartResponse>(ApprovalJson.Options);
        check("批量发起逐项返回成功审批单或可修正错误并保留原序号",
            response.StatusCode == HttpStatusCode.OK && result?.Results.Count == 4 &&
            result.Results[0] is { Index: 0, Instance: not null, Error: null } &&
            result.Results[1] is { Index: 1, Instance: null, Error.Code: ErrorCodes.ValidationFailed } &&
            result.Results[2] is { Index: 2, Instance: null, Error.Code: ErrorCodes.ValidationFailed } &&
            result.Results[3] is { Index: 3, Instance: not null, Error: null });
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var instances = await db.Instances.AsNoTracking().Where(item =>
                item.BusinessKey.StartsWith("BATCH-")).ToArrayAsync();
            check("批量发起中间项目失败不回滚其他项目且失败项不落库",
                instances.Length == 2 && instances.All(item => item.Status == EngineInstanceStatuses.Running) &&
                instances.Any(item => item.BusinessKey == "BATCH-1") &&
                instances.Any(item => item.BusinessKey == "BATCH-3"));
        }

        using var repeatRequest = BatchRequest(batch, apiKey, "batch-start");
        using var repeatResponse = await http.SendAsync(repeatRequest);
        var repeated = await repeatResponse.Content.ReadFromJsonAsync<OpenBatchStartResponse>(ApprovalJson.Options);
        check("重试批量发起时已成功单据返回原在途审批单且失败项可独立重试",
            repeatResponse.StatusCode == HttpStatusCode.OK && repeated?.Results.Count == 4 &&
            repeated.Results[0].Instance?.Id == result?.Results[0].Instance?.Id &&
            repeated.Results[3].Instance?.Id == result?.Results[3].Instance?.Id &&
            repeated.Results[1].Error?.Code == ErrorCodes.ValidationFailed);
    }

    /// <summary>
    /// 构造带稳定批次 ID 的批量发起请求。
    /// </summary>
    /// <param name="batch">批量发起数据。</param>
    /// <param name="apiKey">已授权应用的 API Key。</param>
    /// <param name="requestId">稳定批次请求 ID。</param>
    /// <returns>可发送的 HTTP 请求。</returns>
    private static HttpRequestMessage BatchRequest(OpenBatchStartRequest batch, string apiKey, string requestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OpenApiRoutes.InstancesBatch)
        {
            Content = JsonContent.Create(batch),
        };
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        request.Headers.Add(HeaderNames.RequestId, requestId);
        return request;
    }
}
