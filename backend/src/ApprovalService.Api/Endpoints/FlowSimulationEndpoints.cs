using System.Security.Claims;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 本地流程管理员的纯内存模拟接口。
/// </summary>
internal static class FlowSimulationEndpoints
{
    /// <summary>
    /// 登记 JWT 保护的创建、读取、逐步推进和删除端点。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapFlowSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.AdminFlowSimulations, (HttpContext http, FlowSimulationCreateRequest request,
            FlowSimulationAppService service, CancellationToken cancellationToken) =>
            service.CreateAsync(request, PersonId(http), IsLocal(http), cancellationToken)).RequireAuthorization();
        app.MapGet(ApiRoutes.AdminFlowSimulationById, (HttpContext http, string id,
            FlowSimulationAppService service, CancellationToken cancellationToken) =>
            service.GetAsync(id, PersonId(http), IsLocal(http), cancellationToken)).RequireAuthorization();
        app.MapPost(ApiRoutes.AdminFlowSimulationSteps, (HttpContext http, string id,
            FlowSimulationStepRequest request, FlowSimulationAppService service, CancellationToken cancellationToken) =>
            service.StepAsync(id, request, PersonId(http), IsLocal(http), cancellationToken)).RequireAuthorization();
        app.MapDelete(ApiRoutes.AdminFlowSimulationById, async (HttpContext http, string id,
            FlowSimulationAppService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, PersonId(http), IsLocal(http), cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 从已验证 JWT 读取内部人员 ID。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>当前人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");

    /// <summary>
    /// 嵌入会话无权模拟，即使对应人员也是管理员。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>本地账号会话为真。</returns>
    private static bool IsLocal(HttpContext http) =>
        http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) == "local";
}
