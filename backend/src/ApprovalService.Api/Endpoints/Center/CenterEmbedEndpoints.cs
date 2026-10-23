using System.Security.Claims;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints.Center;

/// <summary>
/// 已验证嵌入会话读取允许来源的接口，不接受路径或查询参数自报应用。
/// </summary>
internal static class CenterEmbedEndpoints
{
    /// <summary>
    /// 登记仅嵌入会话可用的来源查询。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>原路由登记器。</returns>
    public static IEndpointRouteBuilder MapCenterEmbedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(CenterRoutes.EmbedContext, GetContextAsync).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 从已验证 JWT 的应用声明读取当前允许来源。
    /// </summary>
    /// <param name="http">当前已认证请求。</param>
    /// <param name="apps">应用来源查询用例。</param>
    /// <param name="environment">当前部署环境。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>实际应用及其当前允许来源。</returns>
    private static async Task<CenterEmbedContextResponse> GetContextAsync(HttpContext http,
        ClientAppAppService apps, IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var scope = CenterQueryEndpoints.Scope(http);
        var kind = http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim);
        var externalUserId = http.User.FindFirstValue(JwtIdentityTokenService.ExternalUserIdClaim);
        if (kind != JwtIdentityTokenService.EmbedSessionKind ||
            string.IsNullOrWhiteSpace(scope.ClientAppCode) || string.IsNullOrWhiteSpace(externalUserId))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "只有已验证的内嵌会话可以读取允许来源。");
        }

        var origins = await apps.GetAllowedEmbedOriginsAsync(scope.ClientAppCode,
            environment.IsDevelopment(), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return new CenterEmbedContextResponse(scope.ClientAppCode, origins);
    }
}
