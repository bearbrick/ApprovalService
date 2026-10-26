using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 仅本地组织管理员可用的打印页眉品牌配置接口。
/// </summary>
internal static class SystemBrandingEndpoints
{
    /// <summary>
    /// 登记读取、保存和图片访问路由。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>原路由登记器。</returns>
    public static IEndpointRouteBuilder MapSystemBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.AdminSystemBranding, (SystemBrandingAppService service,
            CancellationToken cancellationToken) => service.GetAsync(cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPut(ApiRoutes.AdminSystemBranding, (HttpContext http, UpdateSystemBrandingRequest request,
            SystemBrandingAppService service, CancellationToken cancellationToken) =>
            service.SaveNameAsync(request, PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminSystemLogo, async (HttpContext http, SystemBrandingAppService service,
            CancellationToken cancellationToken) =>
        {
            var image = await service.ReadLogoAsync(cancellationToken);
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
            return Results.File(image.Bytes, image.ContentType);
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(ApiRoutes.AdminSystemLogo, (HttpContext http, UploadSystemLogoRequest request,
            SystemBrandingAppService service, CancellationToken cancellationToken) =>
            service.SaveLogoAsync(request, PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapDelete(ApiRoutes.AdminSystemLogo, (HttpContext http, [FromBody] DeleteSystemLogoRequest request,
            SystemBrandingAppService service, CancellationToken cancellationToken) =>
            service.DeleteLogoAsync(request, PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }

    /// <summary>
    /// 从已核验的本地管理员令牌读取内部人员 ID。
    /// </summary>
    /// <param name="http">当前 HTTP 请求。</param>
    /// <returns>人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
}
