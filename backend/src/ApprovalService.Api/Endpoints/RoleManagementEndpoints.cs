using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 仅本地组织管理员可用的角色管理入口。
/// </summary>
internal static class RoleManagementEndpoints
{
    /// <summary>
    /// 登记角色目录、名称和成员路由。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>原登记器。</returns>
    public static IEndpointRouteBuilder MapRoleManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.AdminRoles, (HttpContext http, RoleManagementAppService service,
            CancellationToken cancellationToken) => service.ListAsync(PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(ApiRoutes.AdminRoles, (HttpContext http, CreateManagedRoleRequest request,
            RoleManagementAppService service, CancellationToken cancellationToken) =>
            service.CreateAsync(request, PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminRoleByCode, (string code, HttpContext http, RoleManagementAppService service,
            CancellationToken cancellationToken) => service.GetAsync(code, PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPut(ApiRoutes.AdminRoleByCode, (string code, HttpContext http, UpdateManagedRoleRequest request,
            RoleManagementAppService service, CancellationToken cancellationToken) =>
            service.RenameAsync(code, request, PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminRoleMembers, (string code, HttpContext http, RoleManagementAppService service,
            CancellationToken cancellationToken) => service.GetAsync(code, PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPut(ApiRoutes.AdminRoleMembers, (string code, HttpContext http, UpdateRoleMembersRequest request,
            RoleManagementAppService service, CancellationToken cancellationToken) =>
            service.MembersAsync(code, request, PersonId(http), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }

    /// <summary>
    /// 从已通过本地会话策略的请求提取内部人员。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>内部人员 ID。</returns>
    private static string PersonId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
}
