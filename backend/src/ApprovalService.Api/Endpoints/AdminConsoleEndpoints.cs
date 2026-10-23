using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 只允许本地组织管理员访问的管理后台读取接口。
/// </summary>
internal static class AdminConsoleEndpoints
{
    /// <summary>
    /// 登记管理端点并对每个端点单独应用组织管理员策略。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapAdminConsoleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.AdminSession, (HttpContext http) => new AdminSessionResponse
        {
            PersonId = PersonId(http),
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminDirectory, (AdminConsoleAppService service, CancellationToken cancellationToken) =>
            service.GetDirectoryAsync(cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminClientApps, (AdminConsoleAppService service, CancellationToken cancellationToken) =>
            service.GetClientAppsAsync(cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminAppEmbedOrigins, (string code, AdminConsoleAppService service,
            CancellationToken cancellationToken) => service.GetEmbedOriginsAsync(code, cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPut(ApiRoutes.AdminAppEmbedOrigins, (HttpContext http, string code,
            UpdateAdminEmbedOriginsRequest request, AdminConsoleAppService service,
            CancellationToken cancellationToken) => service.SetEmbedOriginsAsync(code, request,
                PersonId(http), DateTimeOffset.UtcNow, cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }

    /// <summary>
    /// 只读取通过 JWT 验签的内部人员身份。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <returns>已验证的人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
}
