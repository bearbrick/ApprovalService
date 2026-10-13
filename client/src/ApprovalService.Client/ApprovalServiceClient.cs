using System.Net.Http;
using ApprovalService.Client.Internal;

namespace ApprovalService.Client;

/// <summary>
/// 审批流服务的客户端。应用内建一个实例、多线程共享即可。
/// </summary>
/// <remarks>
/// 骨架阶段只有 <see cref="GetInfoAsync"/>；发起、查询、撤回、重新提交、待办、审批操作、免登录地址、推送组织架构、上传附件、回调验签
/// 按接口清单（设计书第 13 节）在 SDK 任务里补齐。
/// </remarks>
public sealed class ApprovalServiceClient : IDisposable
{
    /// <summary>
    /// 发请求用的 HttpClient。
    /// </summary>
    private readonly HttpClient http;

    /// <summary>
    /// 客户端设置。
    /// </summary>
    private readonly ApprovalServiceClientOptions options;

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
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.BaseAddress == null)
        {
            throw new ArgumentException("必须指定服务地址 BaseAddress。", nameof(options));
        }

        http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        // 地址末尾补上斜杠，相对路径才会拼在后面而不是替换最后一段。
        var address = options.BaseAddress.ToString();
        http.BaseAddress = new Uri(address.EndsWith("/", StringComparison.Ordinal) ? address : address + "/");
        http.Timeout = options.Timeout;
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
    /// <returns>解析后的 JSON（对象是字典，数组是列表）；响应体为空时为 null。</returns>
    /// <exception cref="ApprovalServiceException">服务返回错误、连不上或超时。</exception>
    private async Task<object?> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using (var request = new HttpRequestMessage(method, path))
        {
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                request.Headers.TryAddWithoutValidation(Protocol.ApiKey, options.ApiKey);
            }

            request.Headers.TryAddWithoutValidation(Protocol.RequestId, Guid.NewGuid().ToString("N"));
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // 调用方没取消却被取消，是 HttpClient 的超时。
                throw new ApprovalServiceException(ApprovalErrorCodes.Timeout, $"等待审批流服务响应超过 {options.Timeout.TotalSeconds:0} 秒。", innerException: ex);
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
    /// <returns>属性值；没有或不是字符串时为空字符串。</returns>
    private static string Text(Dictionary<string, object?> root, string name) =>
        root.TryGetValue(name, out var value) && value is string text ? text : "";
}
