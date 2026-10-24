using System.Security.Claims;
using ApprovalService.Application.Center;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints.Center;

/// <summary>
/// 登录人员的审批中心查询路由，不接受外部人员 ID 或 API Key 作为会话替代。
/// </summary>
internal static class CenterQueryEndpoints
{
    /// <summary>
    /// 注册只读查询并强制登录认证。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>同一路由登记器。</returns>
    public static IEndpointRouteBuilder MapCenterQueryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(CenterRoutes.Instances, ListAsync).RequireAuthorization();
        app.MapGet(CenterRoutes.Instance, FindAsync).RequireAuthorization();
        app.MapPost(CenterRoutes.MarkRead, MarkReadAsync).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 从可信会话构造范围，并传递只读列表参数。
    /// </summary>
    /// <param name="http">已执行认证的请求。</param>
    /// <param name="service">中心查询用例。</param>
    /// <param name="section">列表分组。</param>
    /// <param name="flowCode">流程编码筛选。</param>
    /// <param name="status">状态筛选。</param>
    /// <param name="search">标题、编号或可见字段内容搜索。</param>
    /// <param name="from">发起时间起点。</param>
    /// <param name="to">发起时间终点。</param>
    /// <param name="skip">跳过数量。</param>
    /// <param name="take">页大小。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已授权列表。</returns>
    private static Task<CenterListResponse> ListAsync(HttpContext http, CenterQueryAppService service,
        string? section, string? flowCode, string? status, string? search, DateTimeOffset? from,
        DateTimeOffset? to, int? skip, int? take, CancellationToken cancellationToken) => service.ListAsync(
            Scope(http), new CenterQuery
            {
                Section = section ?? CenterSections.Todo,
                FlowCode = flowCode,
                Status = status,
                Search = search,
                From = from,
                To = to,
                Skip = skip ?? 0,
                Take = take ?? 20,
            }, cancellationToken);

    /// <summary>
    /// 在可信会话范围内读取单据详情。
    /// </summary>
    /// <param name="http">已执行认证的请求。</param>
    /// <param name="id">审批单 ID。</param>
    /// <param name="service">中心查询用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已授权详情。</returns>
    private static Task<CenterDetailResponse> FindAsync(HttpContext http, string id, CenterQueryAppService service,
        CancellationToken cancellationToken) => service.FindAsync(Scope(http), id, cancellationToken);

    /// <summary>
    /// 重新核对可信登录范围后标记当前人员已看过的活动。
    /// </summary>
    /// <param name="http">已执行认证的请求。</param>
    /// <param name="id">审批单 ID。</param>
    /// <param name="input">本次详情实际显示的活动序号。</param>
    /// <param name="service">中心查询用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>成功时无响应体。</returns>
    private static async Task<IResult> MarkReadAsync(HttpContext http, string id, CenterReadRequest input,
        CenterQueryAppService service,
        CancellationToken cancellationToken)
    {
        await service.MarkReadAsync(Scope(http), id, input, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// 仅从认证声明取人员和应用，拒绝不完整或未知来源的会话。
    /// </summary>
    /// <param name="http">已执行 JWT 验证的请求。</param>
    /// <returns>可信人员与应用范围。</returns>
    internal static CenterScope Scope(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        var user = http.User;
        var personId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var kind = user.FindFirstValue(JwtIdentityTokenService.SessionKindClaim);
        var app = user.FindFirstValue(JwtIdentityTokenService.ClientAppCodeClaim);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(personId) ||
            (kind != "local" && kind != "embed") || (kind == "embed" && string.IsNullOrWhiteSpace(app)) ||
            (kind == "local" && app is not null))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "登录身份不完整，请重新登录。");
        }
        return new CenterScope { PersonId = personId, ClientAppCode = app };
    }
}
