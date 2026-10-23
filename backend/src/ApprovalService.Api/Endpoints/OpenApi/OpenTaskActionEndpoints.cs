using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Endpoints.OpenApi;

/// <summary>
/// 业务系统经 API Key 代表已绑定人员处理本应用人工任务。
/// </summary>
public static class OpenTaskActionEndpoints
{
    /// <summary>
    /// 登记人工任务操作路由。
    /// </summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>原应用。</returns>
    public static WebApplication MapOpenTaskActionEndpoints(this WebApplication app)
    {
        app.MapPost(OpenApiRoutes.TaskActionCatchAll, async (string path, OpenTaskActionRequest request, HttpContext http,
            ClientAppAppService clients, OpenTaskActionAppService actions, CancellationToken cancellationToken) =>
        {
            if (!path.EndsWith("/actions", StringComparison.Ordinal) || path.Length <= "/actions".Length)
            {
                throw new ApprovalService.Domain.Shared.UserFriendlyException(ErrorCodes.NotFound,
                    "任务操作接口地址不存在。");
            }

            // 引擎任务 ID 自带斜杠；同时兼容调用端把整个 ID 编码为单个路径段。
            var id = Uri.UnescapeDataString(path[..^"/actions".Length]);
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            return Results.Ok(await actions.ExecuteAsync(client, id, request,
                http.Response.Headers[HeaderNames.RequestId].ToString(), cancellationToken));
        });
        return app;
    }
}
