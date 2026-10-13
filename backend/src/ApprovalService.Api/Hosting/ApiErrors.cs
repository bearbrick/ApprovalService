using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using Microsoft.AspNetCore.Diagnostics;

namespace ApprovalService.Api.Hosting;

/// <summary>
/// 统一写错误响应：所有接口出错都返回 <see cref="ApiErrorResponse"/>，带上链路 ID（AGENTS.md R6）。
/// </summary>
internal static class ApiErrors
{
    /// <summary>
    /// 写一个错误响应。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <param name="status">HTTP 状态码。</param>
    /// <param name="code">错误码。</param>
    /// <param name="message">给人看的中文说明。</param>
    /// <param name="errors">逐项的错误；没有时为 null。</param>
    /// <returns>写完时完成。</returns>
    public static Task WriteAsync(HttpContext http, int status, string code, string message, IReadOnlyList<ApiFieldError>? errors = null)
    {
        RequestTraceMiddleware.WriteHeaders(http);
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new ApiErrorResponse(code, message, errors, RequestTraceMiddleware.TraceId(http)), ApprovalJson.Options);
    }

    /// <summary>
    /// 错误码对应的 HTTP 状态码。
    /// </summary>
    /// <param name="code">错误码。</param>
    /// <returns>401、403、404、409、500，其余为 400。</returns>
    public static int StatusFor(string code) => code switch
    {
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        ErrorCodes.InternalError => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>
    /// 异常处理：能修正的问题按错误码返回；请求体不是 JSON 返回 INVALID_JSON；其余是程序缺陷，返回 INTERNAL_ERROR（异常由框架写日志）。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <returns>写完时完成。</returns>
    public static Task WriteUnhandledAsync(HttpContext http) => http.Features.Get<IExceptionHandlerFeature>()?.Error switch
    {
        UserFriendlyException friendly => WriteAsync(http, StatusFor(friendly.Code), friendly.Code, friendly.Message),
        BadHttpRequestException bad when bad.InnerException is System.Text.Json.JsonException =>
            WriteAsync(http, StatusCodes.Status400BadRequest, ErrorCodes.InvalidJson, "请求体不是合法的 JSON。"),
        _ => WriteAsync(http, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "服务内部错误，请带上 traceId 反馈给服务方。"),
    };

    /// <summary>
    /// 没有响应体的错误状态（如地址不存在）补上统一格式的错误体。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <returns>写完时完成。</returns>
    public static Task WriteStatusAsync(HttpContext http) => http.Response.StatusCode switch
    {
        StatusCodes.Status404NotFound => WriteAsync(http, StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"接口地址不存在：{http.Request.Path}"),
        StatusCodes.Status405MethodNotAllowed => WriteAsync(http, StatusCodes.Status405MethodNotAllowed, ErrorCodes.NotFound, $"接口不支持 {http.Request.Method} 方法：{http.Request.Path}"),
        _ => Task.CompletedTask,
    };
}
