using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Endpoints.OpenApi;

/// <summary>
/// 业务系统以 API Key 单张或批量发起审批，人员和分支快照由服务端解析。
/// </summary>
public static class OpenStartEndpoints
{
    /// <summary>
    /// 登记单张和逐项独立的批量发起路由。
    /// </summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>原应用，供链式调用。</returns>
    public static WebApplication MapOpenStartEndpoints(this WebApplication app)
    {
        app.MapPost(OpenApiRoutes.Instances, async (OpenStartRequest request, HttpContext http,
            ClientAppAppService clients, OpenApprovalStartAppService starts, CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            var requestId = http.Response.Headers[HeaderNames.RequestId].ToString();
            return Results.Ok(await starts.StartAsync(client, request, requestId, cancellationToken));
        });
        app.MapPost(OpenApiRoutes.InstancesBatch, async (OpenBatchStartRequest request, HttpContext http,
            ClientAppAppService clients, OpenApprovalStartAppService starts, CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            var requestId = http.Response.Headers[HeaderNames.RequestId].ToString();
            return Results.Ok(await starts.StartBatchAsync(client, request, requestId, cancellationToken));
        });
        return app;
    }
}
