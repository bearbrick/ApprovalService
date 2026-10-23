using ApprovalService.Application.Center;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints.Center;

/// <summary>
/// 审批中心的人员 JWT 任务操作路由。
/// </summary>
internal static class CenterActionEndpoints
{
    /// <summary>
    /// 登记单项及逐项独立的批量任务命令。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>同一路由登记器。</returns>
    public static IEndpointRouteBuilder MapCenterActionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(CenterRoutes.TaskActionCatchAll, ExecuteAsync).RequireAuthorization();
        app.MapPost(CenterRoutes.BatchApprove, BatchApproveAsync).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 从认证声明取身份，向统一任务用例提交单项操作。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <param name="path">含任务 ID 与操作尾段的捕获路径。</param>
    /// <param name="input">命令参数。</param>
    /// <param name="service">人员操作用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>操作后仍经授权的详情。</returns>
    private static Task<CenterTaskActionResponse> ExecuteAsync(HttpContext http, string path,
        CenterTaskActionRequest input, CenterActionAppService service, CancellationToken cancellationToken)
    {
        if (!path.EndsWith("/actions", StringComparison.Ordinal) || path.Length <= "/actions".Length)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "任务操作接口地址不存在。");
        }
        // 引擎任务 ID 自带斜杠，兼容整个 ID 编码为单路径段的网页请求。
        var taskId = Uri.UnescapeDataString(path[..^"/actions".Length]);
        return service.ExecuteAsync(CenterQueryEndpoints.Scope(http), taskId, input,
            http.Response.Headers[HeaderNames.RequestId].ToString(), false, cancellationToken);
    }

    /// <summary>
    /// 同一会话的多项同意逐项执行并返回成功或可修正的错误。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <param name="input">批量任务列表。</param>
    /// <param name="service">人员操作用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>逐项结果。</returns>
    private static Task<CenterBatchApproveResponse> BatchApproveAsync(HttpContext http,
        CenterBatchApproveRequest input, CenterActionAppService service, CancellationToken cancellationToken) =>
        service.BatchApproveAsync(CenterQueryEndpoints.Scope(http), input,
            http.Response.Headers[HeaderNames.RequestId].ToString(), cancellationToken);
}
