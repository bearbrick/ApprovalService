using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Endpoints.OpenApi;

/// <summary>
/// 通过 API Key 限制审批单查询只能访问来源接入应用。
/// </summary>
public static class OpenInstanceQueryEndpoints
{
    /// <summary>
    /// 登记审批单详情与业务单号查询路由。
    /// </summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>原应用，供链式调用。</returns>
    public static WebApplication MapOpenInstanceQueryEndpoints(this WebApplication app)
    {
        app.MapGet(OpenApiRoutes.Instance, async (string id, HttpContext http, ClientAppAppService clients,
            OpenApprovalQueryAppService queries, CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await queries.GetInstanceAsync(client, id, cancellationToken));
        });
        app.MapGet(OpenApiRoutes.Instances, async (string? flowCode, string? businessKey, HttpContext http,
            ClientAppAppService clients, OpenApprovalQueryAppService queries, CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await queries.FindInstancesAsync(client, flowCode ?? string.Empty,
                businessKey ?? string.Empty, cancellationToken));
        });
        return app;
    }
}
