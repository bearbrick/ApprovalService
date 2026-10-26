using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 本地组织管理员的工作日历读写端点。
/// </summary>
internal static class WorkCalendarEndpoints
{
    /// <summary>
    /// 登记日历端点并复用实时本地组织管理员授权。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>原登记器。</returns>
    public static IEndpointRouteBuilder MapWorkCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.AdminWorkCalendar, (WorkCalendarAppService service,
            CancellationToken cancellationToken) => service.ReadAsync(cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPut(ApiRoutes.AdminWorkCalendar, (HttpContext http, UpdateWorkCalendarRequest request,
            WorkCalendarAppService service, CancellationToken cancellationToken) => service.SaveAsync(request,
                http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                    throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。"), cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }
}
