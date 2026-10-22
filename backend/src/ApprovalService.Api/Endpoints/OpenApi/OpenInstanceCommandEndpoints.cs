using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Endpoints.OpenApi;

/// <summary>
/// 业务系统以接入应用和已绑定发起人的身份撤回或催办。
/// </summary>
public static class OpenInstanceCommandEndpoints
{
    /// <summary>
    /// 登记实例级操作路由。
    /// </summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>原应用。</returns>
    public static WebApplication MapOpenInstanceCommandEndpoints(this WebApplication app)
    {
        app.MapPost(OpenApiRoutes.InstanceWithdraw, async (string id, OpenInstanceCommandRequest request,
            HttpContext http, ClientAppAppService clients, OpenInstanceCommandAppService commands,
            CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await commands.WithdrawAsync(client, id, request,
                http.Response.Headers[HeaderNames.RequestId].ToString(), cancellationToken));
        });
        app.MapPost(OpenApiRoutes.InstanceRemind, async (string id, OpenInstanceCommandRequest request,
            HttpContext http, ClientAppAppService clients, OpenInstanceCommandAppService commands,
            CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await commands.RemindAsync(client, id, request,
                http.Response.Headers[HeaderNames.RequestId].ToString(), cancellationToken));
        });
        return app;
    }
}
