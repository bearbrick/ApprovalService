using System.Security.Claims;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 本地管理员的运行干预端点，具体范围由服务端重新核对。
/// </summary>
internal static class AdminInterventionEndpoints
{
    /// <summary>
    /// 登记查询和命令；不接受嵌入会话或业务 API Key。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>同一路由登记器。</returns>
    public static IEndpointRouteBuilder MapAdminInterventionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(AdminInterventionRoutes.Instances, (HttpContext http, AdminInterventionAppService service,
            CancellationToken cancellationToken) =>
            service.ListAsync(PersonId(http), IsLocal(http), cancellationToken)).RequireAuthorization();
        app.MapGet(AdminInterventionRoutes.Instance, (HttpContext http, string id,
            AdminInterventionAppService service, CancellationToken cancellationToken) =>
            service.GetAsync(id, PersonId(http), IsLocal(http), cancellationToken)).RequireAuthorization();
        app.MapPost(AdminInterventionRoutes.Commands, ExecuteAsync).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 明确要求调用方提供稳定请求 ID，不能让中间件替重试生成新标识。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="id">审批单 ID。</param>
    /// <param name="input">命令参数。</param>
    /// <param name="service">干预用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>执行成功后无正文。</returns>
    private static async Task<IResult> ExecuteAsync(HttpContext http, string id,
        AdminInterventionRequest input, AdminInterventionAppService service, CancellationToken cancellationToken)
    {
        await service.ExecuteAsync(id, PersonId(http), IsLocal(http),
            http.Request.Headers[HeaderNames.RequestId].ToString(), input, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// 只取已验证 JWT 的人员 ID。
    /// </summary>
    /// <param name="http">请求。</param>
    /// <returns>可信人员 ID。</returns>
    private static string PersonId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");

    /// <summary>
    /// 检查 JWT 是否属于本地登录。
    /// </summary>
    /// <param name="http">请求。</param>
    /// <returns>本地会话时为真。</returns>
    private static bool IsLocal(HttpContext http) =>
        http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) == "local";
}
