using System.Security.Claims;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 本地组织管理员或该应用管理员排入签名测试回调。
/// </summary>
internal static class TestCallbackEndpoints
{
    /// <summary>
    /// 登记要求有效 JWT 的接入测试路由，具体应用范围由应用层重验。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapTestCallbackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.AdminAppTestCallback, (HttpContext http, string code,
            AdminTestCallbackRequest request, TestCallbackAppService service, CancellationToken cancellationToken) =>
            service.EnqueueAsync(code, request, PersonId(http), IsLocal(http), cancellationToken))
            .RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 从已验证 JWT 读取内部人员 ID。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>可信内部人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");

    /// <summary>
    /// 嵌入会话不允许触发接入测试。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>本地账号会话为真。</returns>
    private static bool IsLocal(HttpContext http) =>
        http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) == "local";
}
