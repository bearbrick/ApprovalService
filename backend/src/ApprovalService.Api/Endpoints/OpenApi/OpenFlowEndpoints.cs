using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Endpoints.OpenApi;

/// <summary>
/// 只公开已发布且接入应用已授权的流程。
/// </summary>
public static class OpenFlowEndpoints
{
    /// <summary>
    /// 登记流程列表和审批数据定义接口。
    /// </summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>原应用，供链式调用。</returns>
    public static WebApplication MapOpenFlowEndpoints(this WebApplication app)
    {
        app.MapGet(OpenApiRoutes.Flows, async (HttpContext http, ClientAppAppService clients,
            FlowCatalogAppService flows, CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await flows.ListAsync(client, cancellationToken));
        });
        app.MapGet(OpenApiRoutes.FlowSchema, async (string code, HttpContext http, ClientAppAppService clients,
            FlowCatalogAppService flows, CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await flows.GetSchemaAsync(client, code, cancellationToken));
        });
        app.MapPost(OpenApiRoutes.FlowPreview, async (string code, OpenFlowPreviewRequest request,
            HttpContext http, ClientAppAppService clients, OpenFlowPreviewAppService previews,
            CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await previews.PreviewAsync(client, code, request,
                http.Response.Headers[HeaderNames.RequestId].ToString(), cancellationToken));
        });
        return app;
    }
}
