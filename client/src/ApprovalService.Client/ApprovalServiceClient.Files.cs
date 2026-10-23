using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using ApprovalService.Client.Internal;

namespace ApprovalService.Client;

/// <summary>
/// 接入应用上传附件，以及按应用或已登录人员身份下载附件。
/// </summary>
public sealed partial class ApprovalServiceClient
{
    /// <summary>
    /// 以当前接入应用 API Key 上传原始文件内容。
    /// </summary>
    /// <param name="fileName">显示文件名。</param>
    /// <param name="content">可读文件流；调用结束后仍由调用方负责释放。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>服务端识别的类型、大小和摘要。</returns>
    public async Task<ApprovalUploadedFile> UploadFileAsync(string fileName, Stream content,
        CancellationToken cancellationToken = default)
    {
        Required(fileName, nameof(fileName));
        RequireNotNull(content, nameof(content));
        if (!content.CanRead)
        {
            throw new ArgumentException("上传附件需要可读文件流。", nameof(content));
        }

        var path = Relative(AuxiliaryPaths.Files) + "?fileName=" + Encode(fileName, nameof(fileName));
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StreamContent(new NonClosingReadStream(content));
        request.Headers.TryAddWithoutValidation(Protocol.ApiKey, Required(apiKey ?? string.Empty, nameof(apiKey)));
        request.Headers.TryAddWithoutValidation(Protocol.RequestId, ValidRequestId(null));
        using var response = await SendFileRequestAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        CheckFileResponse(response, body);
        try
        {
            return AdvancedApiJson.UploadedFile(JsonText.Parse(body));
        }
        catch (FormatException ex)
        {
            throw new ApprovalServiceException(ApprovalErrorCodes.InternalError,
                "附件上传响应不是合法的 JSON。", (int)response.StatusCode, innerException: ex);
        }
    }

    /// <summary>
    /// 以应用 API Key 下载本应用上传且尚未关联审批单的附件。
    /// </summary>
    /// <param name="fileId">附件 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>附件内容及响应元数据。</returns>
    public Task<ApprovalDownloadedFile> DownloadUnlinkedFileAsync(string fileId,
        CancellationToken cancellationToken = default) => DownloadFileCoreAsync(fileId, null, cancellationToken);

    /// <summary>
    /// 以已登录人员的会话令牌下载有权查看的附件。
    /// </summary>
    /// <param name="fileId">附件 ID。</param>
    /// <param name="accessToken">人员登录或嵌入会话的 Bearer 令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>附件内容及响应元数据。</returns>
    public Task<ApprovalDownloadedFile> DownloadFileAsync(string fileId, string accessToken,
        CancellationToken cancellationToken = default) =>
        DownloadFileCoreAsync(fileId, Required(accessToken, nameof(accessToken)), cancellationToken);

    /// <summary>
    /// 先让服务端验权，再在对象存储跳转时去掉所有身份头。
    /// </summary>
    /// <param name="fileId">附件 ID。</param>
    /// <param name="accessToken">人员令牌；为空时以应用 Key 请求未关联文件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载结果。</returns>
    private async Task<ApprovalDownloadedFile> DownloadFileCoreAsync(string fileId, string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ForId(AuxiliaryPaths.FileById, fileId));
        if (accessToken == null)
        {
            request.Headers.TryAddWithoutValidation(Protocol.ApiKey, Required(apiKey ?? string.Empty, nameof(apiKey)));
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        request.Headers.TryAddWithoutValidation(Protocol.RequestId, ValidRequestId(null));
        using var response = await SendFileRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.Found ||
            response.StatusCode == HttpStatusCode.SeeOther || response.StatusCode == HttpStatusCode.TemporaryRedirect)
        {
            var target = response.Headers.Location;
            if (target == null || !target.IsAbsoluteUri ||
                (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
            {
                throw new ApprovalServiceException(ApprovalErrorCodes.InternalError, "附件下载跳转地址无效。");
            }

            // 预签名 URL 自己携带短时授权，绝不复制 API Key、Bearer 或请求 ID。
            using var redirected = new HttpRequestMessage(HttpMethod.Get, target);
            using var external = await SendFileRequestAsync(redirected, cancellationToken).ConfigureAwait(false);
            return await ReadFileResponseAsync(external).ConfigureAwait(false);
        }

        return await ReadFileResponseAsync(response).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取下载响应；错误仍保持 SDK 统一异常格式。
    /// </summary>
    /// <param name="response">服务或对象存储响应。</param>
    /// <returns>内容和文件元数据。</returns>
    private static async Task<ApprovalDownloadedFile> ReadFileResponseAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            CheckFileResponse(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        return new ApprovalDownloadedFile
        {
            Content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false),
            ContentType = response.Content.Headers.ContentType?.MediaType,
            FileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
                response.Content.Headers.ContentDisposition?.FileName,
        };
    }

    /// <summary>
    /// 在文件请求中复用网络异常和超时映射。
    /// </summary>
    /// <param name="request">HTTP 请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>由调用方释放的响应。</returns>
    private async Task<HttpResponseMessage> SendFileRequestAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApprovalServiceException(ApprovalErrorCodes.Timeout,
                $"等待审批流服务响应超过 {timeout.TotalSeconds:0.###} 秒。", innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ApprovalServiceException(ApprovalErrorCodes.NetworkError,
                "连不上审批流服务：" + ex.Message, innerException: ex);
        }
    }

    /// <summary>
    /// 将附件接口错误转为带错误码和链路 ID 的异常。
    /// </summary>
    /// <param name="response">HTTP 响应。</param>
    /// <param name="body">错误正文。</param>
    private static void CheckFileResponse(HttpResponseMessage response, string body)
    {
        if (!response.IsSuccessStatusCode)
        {
            var traceId = response.Headers.TryGetValues(Protocol.TraceId, out var values)
                ? values.FirstOrDefault() : null;
            throw ToException((int)response.StatusCode, body, traceId);
        }
    }
}
