using System.Security.Claims;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Center;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Endpoints.Center;

/// <summary>
/// 本人和组织管理员的审批委托入口，身份只取已验证 JWT。
/// </summary>
internal static class CenterDelegationEndpoints
{
    /// <summary>
    /// 登记委托设置和已有待办逐项转委托接口。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>同一路由登记器。</returns>
    public static IEndpointRouteBuilder MapCenterDelegationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(CenterRoutes.Delegations, ListOwnAsync).RequireAuthorization();
        app.MapPost(CenterRoutes.Delegations, CreateOwnAsync).RequireAuthorization();
        app.MapDelete(CenterRoutes.Delegation, DeleteOwnAsync).RequireAuthorization();
        app.MapPost(CenterRoutes.DelegateTasks, DelegateOwnTasksAsync).RequireAuthorization();
        app.MapGet(CenterRoutes.AdminDelegations, ListAdminAsync)
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(CenterRoutes.AdminDelegations, CreateAdminAsync)
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapDelete(CenterRoutes.AdminDelegation, DeleteAdminAsync)
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(CenterRoutes.AdminDelegateTasks, DelegateAdminTasksAsync)
            .RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }

    /// <summary>
    /// 读取本人规则。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="service">委托用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本人规则。</returns>
    private static Task<IReadOnlyList<CenterDelegationResponse>> ListOwnAsync(HttpContext http,
        CenterDelegationAppService service, CancellationToken cancellationToken) =>
        service.ListAsync(PersonId(http), PersonId(http), false, IsLocal(http), cancellationToken);

    /// <summary>
    /// 设置本人规则。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="input">新规则。</param>
    /// <param name="service">委托用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>保存后的规则。</returns>
    private static Task<CenterDelegationResponse> CreateOwnAsync(HttpContext http, CenterDelegationRequest input,
        CenterDelegationAppService service, CancellationToken cancellationToken) =>
        service.CreateAsync(PersonId(http), PersonId(http), false, IsLocal(http), input, cancellationToken);

    /// <summary>
    /// 删除本人规则。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="id">规则 ID。</param>
    /// <param name="service">委托用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>成功时无正文。</returns>
    private static async Task<IResult> DeleteOwnAsync(HttpContext http, string id,
        CenterDelegationAppService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(PersonId(http), PersonId(http), false, IsLocal(http), id, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// 本人选择已有任务逐项委托。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="input">任务列表。</param>
    /// <param name="service">委托用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>逐项结果。</returns>
    private static Task<IReadOnlyList<CenterDelegateTaskResult>> DelegateOwnTasksAsync(HttpContext http,
        CenterDelegateTasksRequest input, CenterDelegationAppService service, CancellationToken cancellationToken) =>
        service.DelegateTasksAsync(PersonId(http), PersonId(http), false, IsLocal(http), input,
            RequestId(http), cancellationToken);

    /// <summary>
    /// 管理员查看指定人员的规则。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="principalId">委托人 ID。</param>
    /// <param name="service">委托用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>指定人员规则。</returns>
    private static Task<IReadOnlyList<CenterDelegationResponse>> ListAdminAsync(HttpContext http,
        string principalId, CenterDelegationAppService service, CancellationToken cancellationToken) =>
        service.ListAsync(PersonId(http), principalId, true, IsLocal(http), cancellationToken);

    /// <summary>
    /// 管理员为指定人员设置规则。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="principalId">委托人 ID。</param>
    /// <param name="input">新规则。</param>
    /// <param name="service">委托用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>保存后的规则。</returns>
    private static Task<CenterDelegationResponse> CreateAdminAsync(HttpContext http, string principalId,
        CenterDelegationRequest input, CenterDelegationAppService service, CancellationToken cancellationToken) =>
        service.CreateAsync(PersonId(http), principalId, true, IsLocal(http), input, cancellationToken);

    /// <summary>
    /// 管理员删除指定人员的规则。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="principalId">委托人 ID。</param>
    /// <param name="id">规则 ID。</param>
    /// <param name="service">委托用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>成功时无正文。</returns>
    private static async Task<IResult> DeleteAdminAsync(HttpContext http, string principalId, string id,
        CenterDelegationAppService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(PersonId(http), principalId, true, IsLocal(http), id, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// 管理员为指定人员逐项转委托已有待办。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="principalId">委托人 ID。</param>
    /// <param name="input">任务列表与干预原因。</param>
    /// <param name="service">委托用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>逐项结果。</returns>
    private static Task<IReadOnlyList<CenterDelegateTaskResult>> DelegateAdminTasksAsync(HttpContext http,
        string principalId, CenterDelegateTasksRequest input, CenterDelegationAppService service,
        CancellationToken cancellationToken) =>
        service.DelegateTasksAsync(PersonId(http), principalId, true, IsLocal(http), input,
            RequestId(http), cancellationToken);

    /// <summary>
    /// 从 JWT 读取已校验的内部人员 ID。
    /// </summary>
    /// <param name="http">请求。</param>
    /// <returns>内部人员 ID。</returns>
    private static string PersonId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");

    /// <summary>
    /// 判断当前令牌是否属于本地账号。
    /// </summary>
    /// <param name="http">请求。</param>
    /// <returns>本地账号会话为真。</returns>
    private static bool IsLocal(HttpContext http) =>
        http.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) == "local";

    /// <summary>
    /// 读取请求中间件固定的请求 ID。
    /// </summary>
    /// <param name="http">请求。</param>
    /// <returns>请求 ID。</returns>
    private static string RequestId(HttpContext http) => http.Response.Headers[HeaderNames.RequestId].ToString();
}
