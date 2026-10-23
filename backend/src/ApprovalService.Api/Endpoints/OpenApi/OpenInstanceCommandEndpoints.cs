using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Endpoints.OpenApi;

/// <summary>
/// 业务系统以接入应用和已绑定实际操作人的身份控制审批单。
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
        app.MapMethods(OpenApiRoutes.InstanceData, ["PATCH"], async (string id, OpenDataPatchRequest request,
            HttpContext http, ClientAppAppService clients, OpenDataPatchAppService patches,
            CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await patches.PatchAsync(client, id, request,
                http.Response.Headers[HeaderNames.RequestId].ToString(), cancellationToken));
        });
        app.MapPost(OpenApiRoutes.InstanceWithdraw, async (string id, OpenInstanceCommandRequest request,
            HttpContext http, ClientAppAppService clients, OpenInstanceCommandAppService commands,
            CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await commands.WithdrawAsync(client, id, request,
                http.Response.Headers[HeaderNames.RequestId].ToString(), cancellationToken));
        });
        app.MapPost(OpenApiRoutes.InstanceResubmit, async (string id, OpenResubmitRequest request,
            HttpContext http, ClientAppAppService clients, OpenResubmitAppService submissions,
            CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await submissions.SubmitAsync(client, id, request,
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
        app.MapPost(OpenApiRoutes.InstanceTerminate, async (string id, OpenInstanceCommandRequest request,
            HttpContext http, ClientAppAppService clients, OpenInstanceCommandAppService commands,
            CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await commands.TerminateAsync(client, id, request,
                http.Response.Headers[HeaderNames.RequestId].ToString(), cancellationToken));
        });
        return app;
    }
}
