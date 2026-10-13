using System.Net;
using System.Text;

namespace ApprovalService.Client.Checks;

/// <summary>
/// SDK 的行为（不连真实服务，用假的 HTTP 处理器）：请求头、成功响应解析、错误响应和网络错误转成带错误码的异常。
/// </summary>
internal static class ClientChecks
{
    /// <summary>
    /// 假服务的地址。
    /// </summary>
    private static readonly Uri FakeAddress = new("http://approval.test/base");

    /// <summary>
    /// 运行检查。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    /// <returns>检查做完时完成。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        HttpRequestMessage? seen = null;
        using (var handler = new FakeHandler(request =>
        {
            seen = request;
            return Json(HttpStatusCode.OK, """{"name":"ApprovalService","version":"0.1.0","apiVersion":"v1"}""");
        }))
        using (var client = new ApprovalServiceClient(new ApprovalServiceClientOptions { BaseAddress = FakeAddress, ApiKey = "key-123" }, handler))
        {
            var info = await client.GetInfoAsync();
            check("服务信息解析出名称、版本和接口版本", info is { Name: "ApprovalService", Version: "0.1.0", ApiVersion: "v1" });
            check("请求地址拼在服务地址后面（地址末尾没有斜杠也行）", seen?.RequestUri?.ToString() == "http://approval.test/base/api/v1/info");
            check("请求带 API Key 和 32 位请求 ID",
                seen is not null && seen.Headers.GetValues("X-Api-Key").Single() == "key-123" && seen.Headers.GetValues("X-Request-Id").Single().Length == 32);
        }

        var error = await CatchAsync(() => Json(HttpStatusCode.NotFound,
            """{"code":"NOT_FOUND","message":"接口地址不存在：/api/v1/info","traceId":"trace-1"}"""));
        check("统一格式的错误体转成异常：错误码、说明、状态码、链路 ID",
            error is { ErrorCode: ApprovalErrorCodes.NotFound, StatusCode: 404, TraceId: "trace-1" } && error.Message.StartsWith("接口地址不存在", StringComparison.Ordinal));

        var proxyError = await CatchAsync(() => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("<html>401</html>") });
        check("错误响应不是 JSON 时按状态码给错误码", proxyError is { ErrorCode: ApprovalErrorCodes.Unauthorized, StatusCode: 401 });

        var notJson = await CatchAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>别的网站</html>") });
        check("成功响应不是 JSON 时提示确认服务地址", notJson is { ErrorCode: ApprovalErrorCodes.InternalError } && notJson.Message.Contains("服务地址"));

        var network = await CatchAsync(() => throw new HttpRequestException("连接被拒绝"));
        check("连不上服务时错误码是 NETWORK_ERROR", network is { ErrorCode: ApprovalErrorCodes.NetworkError, StatusCode: null });

        var missingAddress = false;
        try
        {
            using var _ = new ApprovalServiceClient(new ApprovalServiceClientOptions());
        }
        catch (ArgumentException)
        {
            missingAddress = true;
        }

        check("没有服务地址时创建客户端就报错", missingAddress);
    }

    /// <summary>
    /// 用假处理器调一次服务信息，返回抛出的异常。
    /// </summary>
    /// <param name="respond">假处理器的响应；可以直接抛异常模拟网络错误。</param>
    /// <returns>抛出的异常；没抛时为 null。</returns>
    private static async Task<ApprovalServiceException?> CatchAsync(Func<HttpResponseMessage> respond)
    {
        using var handler = new FakeHandler(_ => respond());
        using var client = new ApprovalServiceClient(new ApprovalServiceClientOptions { BaseAddress = FakeAddress }, handler);
        try
        {
            await client.GetInfoAsync();
            return null;
        }
        catch (ApprovalServiceException ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// JSON 响应。
    /// </summary>
    /// <param name="status">状态码。</param>
    /// <param name="json">响应体。</param>
    /// <returns>响应。</returns>
    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// 假的 HTTP 处理器：不发网络请求，直接按委托返回响应。
    /// </summary>
    /// <param name="respond">根据请求给出响应。</param>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
