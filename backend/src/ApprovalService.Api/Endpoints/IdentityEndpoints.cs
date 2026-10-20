using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Organization;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 本地登录、账号维护和接入应用嵌入令牌接口。
/// </summary>
internal static class IdentityEndpoints
{
    /// <summary>
    /// 登记身份路由及其不同的认证边界。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.IdentityLogin, async (IdentityLoginRequest request, IIdentityAppService service,
            CancellationToken cancellationToken) => ToResponse(await service.LoginAsync(request.EmployeeNumber,
                request.Password, cancellationToken)));
        app.MapPost(ApiRoutes.IdentityRefresh, async (IdentityRefreshRequest request, IIdentityAppService service,
            CancellationToken cancellationToken) => ToResponse(await service.RefreshAsync(request.RefreshToken,
                cancellationToken)));
        app.MapPost(ApiRoutes.IdentityChangePassword, async (HttpContext http,
            IdentityChangePasswordRequest request, IIdentityAppService service, CancellationToken cancellationToken) =>
        {
            await service.ChangePasswordAsync(PersonId(http), request.CurrentPassword, request.NewPassword,
                cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapPost(ApiRoutes.IdentityProvision, async (IdentityProvisionRequest request,
            IIdentityAppService service, CancellationToken cancellationToken) =>
        {
            await service.ProvisionAccountAsync(request.PersonId, request.TemporaryPassword, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(ApiRoutes.IdentityRevokeAll, async (IdentityRevokeRequest request,
            IIdentityAppService service, CancellationToken cancellationToken) =>
        {
            await service.RevokeAllAsync(request.PersonId, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(ApiRoutes.EmbedTokens, async (HttpContext http, EmbedTokenRequest request,
            ClientAppAppService apps, IOrganizationLookupAppService organization, IIdentityAppService identity,
            CancellationToken cancellationToken) =>
        {
            var appIdentity = await apps.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey], cancellationToken);
            var personId = await organization.ResolveBoundPersonAsync(appIdentity.Code, request.UserId, cancellationToken)
                ?? throw new UserFriendlyException(ErrorCodes.NotFound, "业务人员尚未绑定在职审批人员。");
            var token = await identity.CreateEmbedTokenAsync(appIdentity.Code, request.UserId, personId, request.TargetPath,
                cancellationToken);
            return new EmbedTokenResponse(token.Token, token.ExpiresAt, token.TargetPath);
        });
        app.MapPost(ApiRoutes.EmbedTokenExchange, async (EmbedTokenExchangeRequest request,
            IIdentityAppService service, CancellationToken cancellationToken) =>
            ToResponse(await service.ExchangeEmbedTokenAsync(request.Token, cancellationToken)));
        return app;
    }

    /// <summary>
    /// 将内部令牌资料映射为开放响应。
    /// </summary>
    /// <param name="tokens">应用服务结果。</param>
    /// <returns>契约响应。</returns>
    private static IdentityTokensResponse ToResponse(IdentityTokensDto tokens) =>
        new(tokens.PersonId, tokens.AccessToken, tokens.AccessExpiresAt, tokens.RefreshToken,
            tokens.RefreshExpiresAt, tokens.MustChangePassword, tokens.TargetPath);

    /// <summary>
    /// 只从已认证的名称标识读取人员 ID。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <returns>内部人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
}
