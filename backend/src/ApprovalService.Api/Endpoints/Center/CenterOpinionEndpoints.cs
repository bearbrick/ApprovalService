using ApprovalService.Application.Center;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Endpoints.Center;

/// <summary>
/// 审批中心个人常用意见路由，人员身份只从已验证令牌获取。
/// </summary>
internal static class CenterOpinionEndpoints
{
    /// <summary>
    /// 注册个人常用意见的读取和管理接口。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>同一路由登记器。</returns>
    public static IEndpointRouteBuilder MapCenterOpinionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(CenterRoutes.Opinions, ListAsync).RequireAuthorization();
        app.MapPost(CenterRoutes.Opinions, CreateAsync).RequireAuthorization();
        app.MapPut(CenterRoutes.Opinion, UpdateAsync).RequireAuthorization();
        app.MapDelete(CenterRoutes.Opinion, DeleteAsync).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 读取当前人员意见，嵌入会话也可选择使用。
    /// </summary>
    /// <param name="http">已验证请求。</param>
    /// <param name="service">常用意见用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>当前人员意见。</returns>
    private static Task<IReadOnlyList<CenterOpinionResponse>> ListAsync(HttpContext http,
        CenterOpinionAppService service, CancellationToken cancellationToken) =>
        service.ListAsync(CenterQueryEndpoints.Scope(http), cancellationToken);

    /// <summary>
    /// 保存本地登录人员的新意见。
    /// </summary>
    /// <param name="http">已验证请求。</param>
    /// <param name="input">意见正文。</param>
    /// <param name="service">常用意见用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已保存意见。</returns>
    private static Task<CenterOpinionResponse> CreateAsync(HttpContext http, CenterOpinionRequest input,
        CenterOpinionAppService service, CancellationToken cancellationToken) =>
        service.CreateAsync(CenterQueryEndpoints.Scope(http), input, cancellationToken);

    /// <summary>
    /// 修改本地登录人员拥有的意见。
    /// </summary>
    /// <param name="http">已验证请求。</param>
    /// <param name="id">意见标识。</param>
    /// <param name="input">新正文。</param>
    /// <param name="service">常用意见用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已修改意见。</returns>
    private static Task<CenterOpinionResponse> UpdateAsync(HttpContext http, string id, CenterOpinionRequest input,
        CenterOpinionAppService service, CancellationToken cancellationToken) =>
        service.UpdateAsync(CenterQueryEndpoints.Scope(http), id, input, cancellationToken);

    /// <summary>
    /// 删除本地登录人员拥有的意见。
    /// </summary>
    /// <param name="http">已验证请求。</param>
    /// <param name="id">意见标识。</param>
    /// <param name="service">常用意见用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>成功时无响应体。</returns>
    private static async Task<IResult> DeleteAsync(HttpContext http, string id,
        CenterOpinionAppService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(CenterQueryEndpoints.Scope(http), id, cancellationToken);
        return Results.NoContent();
    }
}
