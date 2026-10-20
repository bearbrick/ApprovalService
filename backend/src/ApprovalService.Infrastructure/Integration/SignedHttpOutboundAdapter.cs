using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 所有业务系统 HTTP 出站请求复用的 HMAC 签名与超时适配层。
/// </summary>
/// <param name="client">由依赖注入管理连接池的 HTTP 客户端。</param>
public sealed class SignedHttpOutboundAdapter(HttpClient client)
{
    /// <summary>
    /// 单次请求默认最大等待时间。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 对原始 JSON 正文签名并发送，非成功状态或超时交给调用者处理。
    /// </summary>
    /// <param name="url">管理端登记的完整目标地址。</param>
    /// <param name="body">已持久化的稳定 JSON 正文。</param>
    /// <param name="signingSecret">本次发送时解密出的密钥。</param>
    /// <param name="requestId">跨系统追踪用请求 ID。</param>
    /// <param name="cancellationToken">宿主取消信号。</param>
    /// <returns>业务系统响应的状态与原始正文。</returns>
    public async Task<OutboundHttpResponse> PostAsync(string url, string body, string signingSecret, string requestId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation(HeaderNames.Signature, Sign(body, signingSecret));
        request.Headers.TryAddWithoutValidation(HeaderNames.RequestId, requestId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultTimeout);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            return new OutboundHttpResponse
            {
                StatusCode = (int)response.StatusCode,
                Body = await response.Content.ReadAsStringAsync(timeout.Token),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("业务系统接口超过五秒未响应。");
        }
    }

    /// <summary>
    /// 计算原始 UTF-8 请求体的协议签名。
    /// </summary>
    /// <param name="body">待发送的原始正文。</param>
    /// <param name="signingSecret">接入应用签名密钥。</param>
    /// <returns>带 sha256 前缀的小写十六进制签名。</returns>
    public static string Sign(string body, string signingSecret)
    {
        var key = Encoding.UTF8.GetBytes(signingSecret);
        var bytes = Encoding.UTF8.GetBytes(body);
        return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(key, bytes));
    }
}

/// <summary>
/// 出站 HTTP 调用的成功响应，仅在业务系统返回 2xx 时构造。
/// </summary>
public sealed class OutboundHttpResponse
{
    /// <summary>
    /// HTTP 状态码。
    /// </summary>
    public int StatusCode
    {
        get; set;
    }

    /// <summary>
    /// 原始响应正文；回调接收方可留空。
    /// </summary>
    public string Body { get; set; } = string.Empty;
}
