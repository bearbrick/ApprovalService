using System.Net.Http;
using ApprovalService.Client.Internal;

namespace ApprovalService.Client;

/// <summary>
/// 审批流服务的客户端。应用内建一个实例、多线程共享即可。
/// </summary>
/// <remarks>
/// 目前覆盖服务信息与首批已确认的开放接口；其余接口按设计书第 13 节在后续 SDK 任务中补齐。
/// </remarks>
public sealed partial class ApprovalServiceClient : IDisposable
{
    /// <summary>
    /// 发请求用的 HttpClient。
    /// </summary>
    private readonly HttpClient http;

    /// <summary>
    /// 创建客户端时固定下来的 API Key，防止共享客户端运行期间配置对象被修改。
    /// </summary>
    private readonly string? apiKey;

    /// <summary>
    /// 创建客户端时固定下来的超时时间。
    /// </summary>
    private readonly TimeSpan timeout;

    /// <summary>
    /// 创建客户端。
    /// </summary>
    /// <param name="options">设置，必须有服务地址。</param>
    public ApprovalServiceClient(ApprovalServiceClientOptions options)
        : this(options, null)
    {
    }

    /// <summary>
    /// 创建客户端，指定发请求的处理器（写单元测试、走代理时用）。
    /// </summary>
    /// <param name="options">设置，必须有服务地址。</param>
    /// <param name="handler">HTTP 处理器；null 表示用默认的。客户端释放时不释放它。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null。</exception>
    /// <exception cref="ArgumentException">没有服务地址。</exception>
    public ApprovalServiceClient(ApprovalServiceClientOptions options, HttpMessageHandler? handler)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        var baseAddress = options.BaseAddress;
        var configuredTimeout = options.Timeout;
        var configuredApiKey = options.ApiKey;
        if (baseAddress == null)
        {
            throw new ArgumentException("必须指定服务地址 BaseAddress。", nameof(options));
        }

        if (!baseAddress.IsAbsoluteUri ||
            (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(baseAddress.UserInfo) ||
            !string.IsNullOrEmpty(baseAddress.Query) ||
            !string.IsNullOrEmpty(baseAddress.Fragment))
        {
            throw new ArgumentException("服务地址必须是完整的 HTTP 或 HTTPS 地址，且不能包含账号、查询参数或片段。", nameof(options));
        }

        if (configuredTimeout != System.Threading.Timeout.InfiniteTimeSpan && configuredTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "等待响应的超时时间必须大于零，或使用无限超时。");
        }

        if (configuredApiKey != null && (string.IsNullOrWhiteSpace(configuredApiKey) || configuredApiKey.IndexOfAny(new[] { '\r', '\n' }) >= 0))
        {
            throw new ArgumentException("API Key 不能为空白，也不能包含换行符。", nameof(options));
        }

        // 客户端可以跨线程复用，因此不保留可变的设置对象作为请求时的依据。
        apiKey = configuredApiKey;
        timeout = configuredTimeout;

        http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        // 地址末尾补上斜杠，相对路径才会拼在后面而不是替换最后一段。
        var address = baseAddress.ToString();
        http.BaseAddress = new Uri(address.EndsWith("/", StringComparison.Ordinal) ? address : address + "/");
        http.Timeout = timeout;
    }

    /// <summary>
    /// 取服务信息，接入时先调它确认地址和版本。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>服务信息。</returns>
    /// <exception cref="ApprovalServiceException">服务返回错误、连不上或超时。</exception>
    public async Task<ServiceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var body = await SendAsync(HttpMethod.Get, "api/v1/info", cancellationToken).ConfigureAwait(false);
        var root = body as Dictionary<string, object?> ?? throw new ApprovalServiceException(ApprovalErrorCodes.InternalError, "服务返回的不是 JSON 对象。");
        return new ServiceInfo(Text(root, "name"), Text(root, "version"), Text(root, "apiVersion"));
    }

    /// <inheritdoc />
    public void Dispose() => http.Dispose();

    /// <summary>
    /// 发一个请求：带上 API Key 和请求 ID；成功时返回解析好的响应体，失败时抛出带错误码的异常。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">相对服务地址的路径，如 api/v1/info。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="payload">可选的 JSON 请求体。</param>
    /// <returns>解析后的 JSON（对象是字典，数组是列表）；响应体为空时为 null。</returns>
    /// <exception cref="ApprovalServiceException">服务返回错误、连不上或超时。</exception>
    private async Task<object?> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken, object? payload = null)
    {
        using (var request = new HttpRequestMessage(method, path))
        {
            if (payload != null)
            {
                request.Content = new ByteArrayContent(JsonText.Serialize(payload));
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }

            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.TryAddWithoutValidation(Protocol.ApiKey, apiKey);
            }

            request.Headers.TryAddWithoutValidation(Protocol.RequestId, Guid.NewGuid().ToString("N"));
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // 调用方没取消却被取消，是 HttpClient 的超时。
                throw new ApprovalServiceException(ApprovalErrorCodes.Timeout, $"等待审批流服务响应超过 {timeout.TotalSeconds:0.###} 秒。", innerException: ex);
            }
            catch (HttpRequestException ex)
            {
                throw new ApprovalServiceException(ApprovalErrorCodes.NetworkError, "连不上审批流服务：" + ex.Message, innerException: ex);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var traceId = response.Headers.TryGetValues(Protocol.TraceId, out var values) ? values.FirstOrDefault() : null;
                if (!response.IsSuccessStatusCode)
                {
                    throw ToException((int)response.StatusCode, text, traceId);
                }

                try
                {
                    return text.Length == 0 ? null : JsonText.Parse(text);
                }
                catch (FormatException ex)
                {
                    // 地址指错（比如指到了别的网站）时常见：返回 200 但内容不是 JSON。
                    throw new ApprovalServiceException(ApprovalErrorCodes.InternalError, "服务返回的不是 JSON，请确认服务地址：" + ex.Message, (int)response.StatusCode, traceId, ex);
                }
            }
        }
    }

    /// <summary>
    /// 把错误响应转成异常：响应体是统一格式的错误体时用里面的错误码和说明，否则按状态码猜。
    /// </summary>
    /// <param name="status">HTTP 状态码。</param>
    /// <param name="text">响应体。</param>
    /// <param name="traceId">响应头里的链路 ID；没有时为 null。</param>
    /// <returns>异常。</returns>
    private static ApprovalServiceException ToException(int status, string text, string? traceId)
    {
        Dictionary<string, object?>? error = null;
        try
        {
            error = JsonText.Parse(text) as Dictionary<string, object?>;
        }
        catch (FormatException)
        {
            // 响应体不是 JSON（如反向代理返回的网页），按状态码处理。
        }

        if (error != null && error.TryGetValue("code", out var code) && code is string errorCode)
        {
            var message = error.TryGetValue("message", out var value) && value is string m ? m : errorCode;
            var bodyTraceId = error.TryGetValue("traceId", out var id) && id is string t ? t : traceId;
            return new ApprovalServiceException(errorCode, message, status, bodyTraceId);
        }

        var guessed = status switch
        {
            401 => ApprovalErrorCodes.Unauthorized,
            403 => ApprovalErrorCodes.Forbidden,
            404 => ApprovalErrorCodes.NotFound,
            405 => ApprovalErrorCodes.NotFound,
            400 => ApprovalErrorCodes.ValidationFailed,
            409 => ApprovalErrorCodes.Conflict,
            _ => ApprovalErrorCodes.InternalError,
        };
        return new ApprovalServiceException(guessed, $"审批流服务返回 {status}。", status, traceId);
    }

    /// <summary>
    /// 取 JSON 对象里的字符串属性。
    /// </summary>
    /// <param name="root">JSON 对象。</param>
    /// <param name="name">属性名。</param>
    /// <returns>非空的属性值。</returns>
    /// <exception cref="ApprovalServiceException">服务没有按契约返回必填属性。</exception>
    private static string Text(Dictionary<string, object?> root, string name)
    {
        if (root.TryGetValue(name, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw new ApprovalServiceException(ApprovalErrorCodes.InternalError, "服务信息缺少必填字段 " + name + "，请确认服务地址和接口版本。");
    }
}
