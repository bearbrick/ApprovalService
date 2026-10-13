using System.Diagnostics;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Hosting;

/// <summary>
/// 给每个请求定下请求 ID 和链路 ID：请求 ID 用调用方传的（不合规就重新生成），链路 ID 用当前的分布式追踪 ID，
/// 都写进响应头，出错时也写进错误体。以后接平台的链路追踪时，传进来的 traceparent 会自动成为这里的链路 ID（设计书第 10 节）。
/// </summary>
/// <param name="next">下一个中间件。</param>
internal sealed class RequestTraceMiddleware(RequestDelegate next)
{
    /// <summary>
    /// 请求 ID 最长多少个字符。
    /// </summary>
    private const int MaxRequestIdLength = 64;

    /// <summary>
    /// 请求 ID 在 HttpContext.Items 里的键。
    /// </summary>
    private const string RequestIdKey = "ApprovalService.RequestId";

    /// <summary>
    /// 链路 ID 在 HttpContext.Items 里的键。
    /// </summary>
    private const string TraceIdKey = "ApprovalService.TraceId";

    /// <summary>
    /// 处理请求。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <returns>后续中间件处理完时完成。</returns>
    public Task InvokeAsync(HttpContext http)
    {
        var requestId = http.Request.Headers[HeaderNames.RequestId].ToString();
        if (!IsValidRequestId(requestId))
        {
            requestId = Guid.NewGuid().ToString("N");
        }

        http.Items[RequestIdKey] = requestId;
        http.Items[TraceIdKey] = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        WriteHeaders(http);
        return next(http);
    }

    /// <summary>
    /// 本次请求的链路 ID；中间件没运行过时用 ASP.NET Core 自己的请求标识。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <returns>链路 ID。</returns>
    public static string TraceId(HttpContext http) => http.Items[TraceIdKey] as string ?? http.TraceIdentifier;

    /// <summary>
    /// 把请求 ID 和链路 ID 写进响应头。异常处理会清空响应头，写错误体前要再调一次。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    public static void WriteHeaders(HttpContext http)
    {
        if (http.Items[RequestIdKey] is string requestId)
        {
            http.Response.Headers[HeaderNames.RequestId] = requestId;
        }

        http.Response.Headers[HeaderNames.TraceId] = TraceId(http);
    }

    /// <summary>
    /// 调用方传的请求 ID 是否能用：不为空、不太长、只含字母数字和 - _ . :（防止往响应头里塞奇怪的内容）。
    /// </summary>
    /// <param name="value">请求头的值。</param>
    /// <returns>能用时为 true。</returns>
    private static bool IsValidRequestId(string value) =>
        value.Length is > 0 and <= MaxRequestIdLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}
