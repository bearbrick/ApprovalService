using System.Security.Claims;
using System.Text;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 当前本地流程管理员的流程草稿和发布接口。
/// </summary>
internal static class FlowManagementEndpoints
{
    /// <summary>
    /// 登记受 JWT 验签保护的流程管理端点，具体流程范围由应用层复核。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapFlowManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.AdminFlows, (HttpContext http, FlowManagementAppService service,
            CancellationToken cancellationToken) =>
            service.ListAsync(PersonId(http), IsLocal(http), cancellationToken)).RequireAuthorization();
        app.MapPost(ApiRoutes.AdminFlows, (HttpContext http, AdminFlowCreateRequest request,
            FlowManagementAppService service, CancellationToken cancellationToken) =>
            service.CreateAsync(request, PersonId(http), IsLocal(http), cancellationToken)).RequireAuthorization();
        app.MapGet(ApiRoutes.AdminFlowByCode, (HttpContext http, string code,
            FlowManagementAppService service, CancellationToken cancellationToken) =>
            service.GetAsync(code, PersonId(http), IsLocal(http), cancellationToken)).RequireAuthorization();
        app.MapPut(ApiRoutes.AdminFlowDraft, (HttpContext http, string code, AdminFlowDraftRequest request,
            FlowManagementAppService service, CancellationToken cancellationToken) =>
            service.SaveDraftAsync(code, request, PersonId(http), IsLocal(http), cancellationToken))
            .RequireAuthorization();
        app.MapPost(ApiRoutes.AdminFlowPublish, (HttpContext http, string code,
            AdminFlowPublishRequest request, FlowManagementAppService service,
            CancellationToken cancellationToken) =>
            service.PublishAsync(code, request, PersonId(http), IsLocal(http), cancellationToken))
            .RequireAuthorization();
        app.MapPut(ApiRoutes.AdminFlowEnabled, (HttpContext http, string code,
            AdminFlowEnabledRequest request, FlowManagementAppService service,
            CancellationToken cancellationToken) =>
            service.SetEnabledAsync(code, request, PersonId(http), IsLocal(http), cancellationToken))
            .RequireAuthorization();
        app.MapPost(ApiRoutes.AdminFlowImportPreview, (HttpContext http,
            AdminFlowImportPreviewRequest request, FlowTransferAppService service,
            CancellationToken cancellationToken) =>
            service.PreviewImportAsync(request, PersonId(http), IsLocal(http), cancellationToken))
            .RequireAuthorization();
        app.MapPost(ApiRoutes.AdminFlowImport, (HttpContext http, AdminFlowImportRequest request,
            FlowTransferAppService service, CancellationToken cancellationToken) =>
            service.ImportAsync(request, PersonId(http), IsLocal(http), cancellationToken))
            .RequireAuthorization();
        app.MapGet(ApiRoutes.AdminFlowExport, async (HttpContext http, string code, string? source,
            int? version, FlowTransferAppService service, CancellationToken cancellationToken) =>
        {
            var xml = await service.ExportAsync(code, source, version, PersonId(http), IsLocal(http),
                cancellationToken);
            return Results.File(Encoding.UTF8.GetBytes(xml), "application/xml", "approval-flow.bpmn");
        }).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 只读取 JWT 已验证的内部人员 ID。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>内部人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");

    /// <summary>
    /// 嵌入会话不能管理流程，即使对应人员拥有组织角色。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <returns>由本地账号签发时为真。</returns>
    private static bool IsLocal(HttpContext http) =>
        http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) == "local";
}
