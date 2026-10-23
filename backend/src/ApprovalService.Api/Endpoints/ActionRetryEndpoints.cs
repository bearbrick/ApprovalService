using System.Security.Claims;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 流程管理员对待处理自动动作的人工重试入口。
/// </summary>
internal static class ActionRetryEndpoints
{
    /// <summary>
    /// 登记只接受本地登录的管理接口。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapActionRetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.AdminActionRetry, async (HttpContext http, AdminActionRetryRequest request,
            ActionRetryAppService service, CancellationToken cancellationToken) =>
        {
            if (http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) !=
                JwtIdentityTokenService.LocalSessionKind)
            {
                throw new UserFriendlyException(ErrorCodes.Forbidden, "仅本地登录的流程管理员可人工重试自动动作。");
            }

            var personId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
            await service.RetryAsync(request.EventId, personId, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();
        return app;
    }
}
