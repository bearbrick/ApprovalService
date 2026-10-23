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
        var settings = new ApprovalServiceClientOptions { BaseAddress = FakeAddress, ApiKey = "key-123" };
        using (var handler = new FakeHandler(request =>
        {
            seen = request;
            return Json(HttpStatusCode.OK, """{"name":"ApprovalService","version":"0.1.0","apiVersion":"v1"}""");
        }))
        using (var client = new ApprovalServiceClient(settings, handler))
        {
            settings.BaseAddress = new Uri("http://changed.test/");
            settings.ApiKey = "changed-key";
            var info = await client.GetInfoAsync();
            check("服务信息解析出名称、版本和接口版本", info is { Name: "ApprovalService", Version: "0.1.0", ApiVersion: "v1" });
            check("请求地址拼在服务地址后面（地址末尾没有斜杠也行）", seen?.RequestUri?.ToString() == "http://approval.test/base/api/v1/info");
            check("请求带 API Key 和 32 位请求 ID",
                seen is not null && seen.Headers.GetValues("X-Api-Key").Single() == "key-123" && seen.Headers.GetValues("X-Request-Id").Single().Length == 32);
            check("客户端创建后修改设置不改变地址和 API Key",
                seen?.RequestUri?.Host == "approval.test" && seen.Headers.GetValues("X-Api-Key").Single() == "key-123");
        }

        var error = await CatchAsync(() => Json(HttpStatusCode.NotFound,
            """{"code":"NOT_FOUND","message":"接口地址不存在：/api/v1/info","traceId":"trace-1"}"""));
        check("统一格式的错误体转成异常：错误码、说明、状态码、链路 ID",
            error is { ErrorCode: ApprovalErrorCodes.NotFound, StatusCode: 404, TraceId: "trace-1" } && error.Message.StartsWith("接口地址不存在", StringComparison.Ordinal));

        var proxyError = await CatchAsync(() => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("<html>401</html>") });
        check("错误响应不是 JSON 时按状态码给错误码", proxyError is { ErrorCode: ApprovalErrorCodes.Unauthorized, StatusCode: 401 });

        var invalidRequest = await CatchAsync(() => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("<html>400</html>") });
        var wrongMethod = await CatchAsync(() => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) { Content = new StringContent("<html>405</html>") });
        check("反向代理返回非 JSON 错误时，400 和 405 仍按契约归类",
            invalidRequest is { ErrorCode: ApprovalErrorCodes.ValidationFailed, StatusCode: 400 } &&
            wrongMethod is { ErrorCode: ApprovalErrorCodes.NotFound, StatusCode: 405 });

        var notJson = await CatchAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>别的网站</html>") });
        check("成功响应不是 JSON 时提示确认服务地址", notJson is { ErrorCode: ApprovalErrorCodes.InternalError } && notJson.Message.Contains("服务地址"));

        var missingField = await CatchAsync(() => Json(HttpStatusCode.OK, """{"name":"ApprovalService","version":"0.1.0"}"""));
        check("成功响应缺少必填字段时报告服务协议异常", missingField is { ErrorCode: ApprovalErrorCodes.InternalError } && missingField.Message.Contains("apiVersion"));

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

        check("不接受相对地址、非 HTTP 地址或带账号和参数的服务地址",
            Rejects(new ApprovalServiceClientOptions { BaseAddress = new Uri("relative/", UriKind.Relative) }) &&
            Rejects(new ApprovalServiceClientOptions { BaseAddress = new Uri("ftp://approval.test/") }) &&
            Rejects(new ApprovalServiceClientOptions { BaseAddress = new Uri("http://user:pass@approval.test/") }) &&
            Rejects(new ApprovalServiceClientOptions { BaseAddress = new Uri("http://approval.test/?debug=1") }) &&
            Rejects(new ApprovalServiceClientOptions { BaseAddress = new Uri("http://approval.test/#fragment") }));

        check("超时和 API Key 在建客户端时验证",
            Rejects(new ApprovalServiceClientOptions { BaseAddress = FakeAddress, Timeout = TimeSpan.Zero }) &&
            Rejects(new ApprovalServiceClientOptions { BaseAddress = FakeAddress, Timeout = TimeSpan.FromSeconds(-2) }) &&
            Rejects(new ApprovalServiceClientOptions { BaseAddress = FakeAddress, ApiKey = "  " }) &&
            Rejects(new ApprovalServiceClientOptions { BaseAddress = FakeAddress, ApiKey = "key\r\nother" }));

        using (var handler = new WaitingHandler())
        {
            var timeoutSettings = new ApprovalServiceClientOptions { BaseAddress = FakeAddress, Timeout = TimeSpan.FromMilliseconds(50) };
            using var client = new ApprovalServiceClient(timeoutSettings, handler);
            timeoutSettings.Timeout = TimeSpan.FromSeconds(5);
            var timedOut = await CaptureAsync(() => client.GetInfoAsync());
            check("响应超时使用创建时的设置，并返回 TIMEOUT", timedOut is ApprovalServiceException { ErrorCode: ApprovalErrorCodes.Timeout, StatusCode: null });
        }

        using (var source = new CancellationTokenSource())
        using (var handler = new CancelHandler(source))
        using (var client = new ApprovalServiceClient(new ApprovalServiceClientOptions { BaseAddress = FakeAddress }, handler))
        {
            var cancelled = await CaptureAsync(() => client.GetInfoAsync(source.Token));
            check("调用方主动取消保持取消语义", cancelled is OperationCanceledException);
        }
    }

    /// <summary>
    /// 判断无效设置是否在创建客户端时被拒绝。
    /// </summary>
    /// <param name="options">待检查的设置。</param>
    /// <returns>创建时抛出参数异常则为 true。</returns>
    private static bool Rejects(ApprovalServiceClientOptions options)
    {
        try
        {
            using var _ = new ApprovalServiceClient(options);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    /// 捕获调用抛出的异常，供不同失败类型的场景检查使用。
    /// </summary>
    /// <param name="call">要运行的异步调用。</param>
    /// <returns>抛出的异常；没有异常时为 null。</returns>
    private static async Task<Exception?> CaptureAsync(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
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

    /// <summary>
    /// 等待取消令牌的假处理器，用来检查客户端自己触发的超时。
    /// </summary>
    private sealed class WaitingHandler : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, "{}");
        }
    }

    /// <summary>
    /// 模拟调用方在请求发出时主动取消。
    /// </summary>
    /// <param name="source">调用方的取消源。</param>
    private sealed class CancelHandler(CancellationTokenSource source) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            source.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }
}
