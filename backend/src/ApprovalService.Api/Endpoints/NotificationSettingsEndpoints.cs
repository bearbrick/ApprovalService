using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Notifications;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 已登录人员维护自身提醒订阅，组织管理员配置每日汇总时间。
/// </summary>
internal static class NotificationSettingsEndpoints
{
    /// <summary>
    /// 登记通知设置的人员与管理员路由。
    /// </summary>
    /// <param name="app">接口路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapNotificationSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.NotificationPreferences, async (HttpContext http,
            INotificationSettingsAppService service, CancellationToken cancellationToken) =>
            (await service.GetPreferencesAsync(PersonId(http), cancellationToken))
                .Select(item => new NotificationPreferenceResponse(item.Category, item.EmailEnabled,
                    item.InAppEnabled)).ToArray()).RequireAuthorization();
        app.MapPut(ApiRoutes.NotificationPreferenceByCategory, async (HttpContext http, string category,
            UpdateNotificationPreferenceRequest request, INotificationSettingsAppService service,
            CancellationToken cancellationToken) =>
        {
            var value = await service.SetPreferenceAsync(PersonId(http), category,
                request.EmailEnabled, request.InAppEnabled, cancellationToken);
            return new NotificationPreferenceResponse(value.Category, value.EmailEnabled, value.InAppEnabled);
        }).RequireAuthorization();
        app.MapGet(ApiRoutes.AdminDailyDigest, async (INotificationSettingsAppService service,
            CancellationToken cancellationToken) =>
            new DailyDigestSettingsResponse(await service.GetDailyDigestTimeAsync(cancellationToken)))
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPut(ApiRoutes.AdminDailyDigest, async (UpdateDailyDigestSettingsRequest request,
            INotificationSettingsAppService service, CancellationToken cancellationToken) =>
            new DailyDigestSettingsResponse(await service.SetDailyDigestTimeAsync(request.LocalTime,
                cancellationToken))).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }

    /// <summary>
    /// 只从已验证会话读取当前人员 ID，不接受请求体自报的身份。
    /// </summary>
    /// <param name="http">已验证的请求上下文。</param>
    /// <returns>内部人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "登录身份不完整，请重新登录。");
}
