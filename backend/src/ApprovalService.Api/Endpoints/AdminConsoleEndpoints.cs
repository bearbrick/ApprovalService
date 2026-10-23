using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Administration;
using ApprovalService.Application.Integration;
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
        app.MapGet(ApiRoutes.AdminBindings, (AdminConsoleAppService service, CancellationToken cancellationToken) =>
            service.GetBindingsAsync(cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminAudit, (AdminConsoleAppService service, CancellationToken cancellationToken) =>
            service.GetAuditAsync(cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminOutbox, (AdminConsoleAppService service, CancellationToken cancellationToken) =>
            service.GetOutboxAsync(cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(ApiRoutes.AdminCallbackRetry, async (HttpContext http, string eventId,
            AdminCallbackRetryRequest request, CallbackDeliveryAppService service,
            CancellationToken cancellationToken) =>
        {
            await service.ResendAdminAsync(eventId, PersonId(http), request.Reason, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapGet(ApiRoutes.AdminClientApps, (AdminConsoleAppService service, CancellationToken cancellationToken) =>
            service.GetClientAppsAsync(cancellationToken))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(ApiRoutes.AdminClientApps, async (HttpContext http,
            AdminClientAppCreateRequest request, ClientAppAppService service,
            CancellationToken cancellationToken) =>
        {
            var secrets = await service.CreateAdminAsync(new CreateClientAppInput
            {
                Code = request.Code,
                Name = request.Name,
                AllowedFlowCodes = request.AllowedFlowCodes,
                CanViewAllTasks = request.CanViewAllTasks,
                CallbackUrl = request.CallbackUrl,
                DataUrl = request.DataUrl,
                ValidationUrl = request.ValidationUrl,
                AssigneeUrl = request.AssigneeUrl,
                ActionUrl = request.ActionUrl,
            }, PersonId(http), request.Reason, DateTimeOffset.UtcNow, cancellationToken);
            return ToSecretsResponse(secrets);
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPut(ApiRoutes.AdminAppEnabled, async (HttpContext http, string code,
            AdminClientAppEnabledRequest request, ClientAppAppService service,
            CancellationToken cancellationToken) =>
        {
            await service.SetEnabledAdminAsync(code, request.Enabled, PersonId(http), request.Reason,
                DateTimeOffset.UtcNow, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(ApiRoutes.AdminAppRotateSecrets, async (HttpContext http, string code,
            AdminClientAppRotateRequest request, ClientAppAppService service,
            CancellationToken cancellationToken) =>
        {
            var secrets = await service.RotateSecretsAdminAsync(code, PersonId(http), request.Reason,
                DateTimeOffset.UtcNow, cancellationToken);
            return ToSecretsResponse(secrets);
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
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

    /// <summary>
    /// 只在当前创建或轮换响应中映射一次性密钥。
    /// </summary>
    /// <param name="secrets">应用服务生成的密钥。</param>
    /// <returns>管理端密钥响应。</returns>
    private static AdminClientAppSecretsResponse ToSecretsResponse(ClientAppSecretsDto secrets) => new()
    {
        Code = secrets.Code,
        ApiKey = secrets.ApiKey,
        SigningSecret = secrets.SigningSecret,
    };
}
