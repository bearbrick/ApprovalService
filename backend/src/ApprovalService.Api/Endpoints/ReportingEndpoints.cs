using System.Globalization;
using System.Security.Claims;
using ApprovalService.Application.Reporting;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 本地登录管理员及部门负责人查询管理范围内的匿名报表。
/// </summary>
internal static class ReportingEndpoints
{
    /// <summary>
    /// 登记四条受 JWT 保护的报表查询接口。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.AdminReportOverview, async (HttpContext http, ReportingQueryAppService service,
            CancellationToken cancellationToken) =>
        {
            var result = await QueryAsync(http, service, cancellationToken);
            return new AdminReportOverviewResponse
            {
                VisibleStartedCount = result.VisibleMetrics.Flows.Sum(item => item.StartedCount),
                VisibleCompletedCount = result.VisibleMetrics.Flows.Sum(item => item.CompletedCount),
                VisiblePendingCount = result.VisibleMetrics.People.Sum(item => item.PendingCount),
                VisibleOverdueCount = result.VisibleMetrics.Nodes.Sum(item => item.OverdueCount),
                ConfidentialStartedCount = result.ConfidentialStartedCount,
                ConfidentialCompletedCount = result.ConfidentialCompletedCount,
            };
        }).RequireAuthorization();
        app.MapGet(ApiRoutes.AdminReportFlows, async (HttpContext http, ReportingQueryAppService service,
            CancellationToken cancellationToken) =>
        {
            var result = await QueryAsync(http, service, cancellationToken);
            return new AdminReportFlowsResponse
            {
                Items = result.VisibleMetrics.Flows.Select(item => new AdminReportFlowItem
                {
                    FlowCode = item.FlowCode,
                    StartedCount = item.StartedCount,
                    CompletedCount = item.CompletedCount,
                    ApprovedCount = item.ApprovedCount,
                    RejectedCount = item.RejectedCount,
                    WithdrawnCount = item.WithdrawnCount,
                    AverageDurationMinutes = item.AverageDurationMinutes,
                    P90DurationMinutes = item.P90DurationMinutes,
                }).ToArray(),
                ConfidentialStartedCount = result.ConfidentialStartedCount,
                ConfidentialCompletedCount = result.ConfidentialCompletedCount,
            };
        }).RequireAuthorization();
        app.MapGet(ApiRoutes.AdminReportNodes, async (HttpContext http, ReportingQueryAppService service,
            CancellationToken cancellationToken) =>
        {
            var result = await QueryAsync(http, service, cancellationToken);
            return new AdminReportNodesResponse
            {
                Items = result.VisibleMetrics.Nodes.Select(item => new AdminReportNodeItem
                {
                    FlowCode = item.FlowCode,
                    NodeId = item.NodeId,
                    CompletedCount = item.CompletedCount,
                    AverageDurationMinutes = item.AverageDurationMinutes,
                    DueCount = item.DueCount,
                    OverdueCount = item.OverdueCount,
                }).ToArray(),
            };
        }).RequireAuthorization();
        app.MapGet(ApiRoutes.AdminReportPeople, async (HttpContext http, ReportingQueryAppService service,
            CancellationToken cancellationToken) =>
        {
            var result = await QueryAsync(http, service, cancellationToken);
            return new AdminReportPeopleResponse
            {
                Items = result.VisibleMetrics.People.Select(item => new AdminReportPersonItem
                {
                    AssigneeId = item.AssigneeId,
                    PendingCount = item.PendingCount,
                    CompletedCount = item.CompletedCount,
                    AverageDurationMinutes = item.AverageDurationMinutes,
                    OverdueCount = item.OverdueCount,
                }).ToArray(),
            };
        }).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 仅取验签后的身份，并让应用服务核对本地会话和管理范围。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <param name="service">报表应用服务。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>授权后的内部报表结果。</returns>
    private static Task<ReportingQueryDto> QueryAsync(HttpContext http, ReportingQueryAppService service,
        CancellationToken cancellationToken)
    {
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
        var sessionKind = http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) ?? string.Empty;
        return service.GetAsync(new ReportingQueryInput
        {
            FromUtc = ReadUtc(http, AdminReportQueryNames.FromUtc),
            UntilUtc = ReadUtc(http, AdminReportQueryNames.UntilUtc),
            FlowCode = ReadOptional(http, AdminReportQueryNames.FlowCode),
            ClientAppCode = ReadOptional(http, AdminReportQueryNames.ClientAppCode),
            InitiatorDepartmentId = ReadOptional(http, AdminReportQueryNames.DepartmentId),
        }, actorId, sessionKind, cancellationToken);
    }

    /// <summary>
    /// 将必填查询时间解析为带零偏移的 UTC 时间。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <param name="name">参数名称。</param>
    /// <returns>UTC 时间。</returns>
    private static DateTimeOffset ReadUtc(HttpContext http, string name)
    {
        var values = http.Request.Query[name];
        var text = values.Count == 1 ? values[0] : null;
        if (text is null || !(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal)) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var value) || value.Offset != TimeSpan.Zero)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, $"{name} 必须是单个 UTC 时间。");
        }

        return value;
    }

    /// <summary>
    /// 读取可选且不重复的文本筛选值。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <param name="name">参数名称。</param>
    /// <returns>未传时为空。</returns>
    private static string? ReadOptional(HttpContext http, string name)
    {
        var values = http.Request.Query[name];
        if (values.Count > 1 || (values.Count == 1 && string.IsNullOrWhiteSpace(values[0])))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, $"{name} 必须是单个非空文本。");
        }

        return values.Count == 0 ? null : values[0];
    }
}
