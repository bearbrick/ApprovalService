using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 本地组织管理员维护接入应用管理员名单的 HTTP 接口。
/// </summary>
internal static class ClientAppAdminEndpoints
{
    /// <summary>
    /// 登记受本地组织管理员策略保护的名单接口。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapClientAppAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.AdminAppAdministrators, (HttpContext http, string code,
            ClientAppAdminAppService service, CancellationToken cancellationToken) =>
            service.GetAsync(code, PersonId(http), IsLocal(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPut(ApiRoutes.AdminAppAdministrators, (HttpContext http, string code,
            UpdateAdminClientAppAdministratorsRequest request, ClientAppAdminAppService service,
            CancellationToken cancellationToken) =>
            service.ReplaceAsync(code, request, PersonId(http), IsLocal(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }

    /// <summary>
    /// 只读取 JWT 验证过的内部人员 ID。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>内部人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");

    /// <summary>
    /// 排除嵌入令牌换取的会话。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>本地账号会话为真。</returns>
    private static bool IsLocal(HttpContext http) =>
        http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) == "local";
}
