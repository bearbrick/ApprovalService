using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 业务系统四类钩子共用的双向验签 HTTP 出口。
/// </summary>
/// <param name="outbound">统一签名和五秒超时适配层。</param>
/// <param name="protector">签名密钥解密器。</param>
public sealed class ApprovalHookClient(SignedHttpOutboundAdapter outbound, IIntegrationSecretProtector protector)
    : IApprovalHookClient
{
    /// <inheritdoc />
    public async Task<JsonElement> InvokeAsync(ClientAppEntity app, string url, object request,
        string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(requestId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "业务系统钩子地址或请求 ID 未配置。");
        }

        var secret = protector.Unprotect(app.SigningSecretCiphertext);
        var body = JsonSerializer.Serialize(request, ApprovalJson.Options);
        OutboundHttpResponse response;
        try
        {
            response = await outbound.PostAsync(url, body, secret, requestId, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TimeoutException or InvalidDataException
            or InvalidOperationException)
        {
            // 不把对端异常正文写到本地日志，也不暴露业务数据给开放接口。
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed,
                error is TimeoutException ? "业务系统钩子五秒内未响应。" : "业务系统钩子调用失败，请稍后重试。");
        }

        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), response.BodyBytes));
        var received = response.Signature ?? string.Empty;
        if (response.RequestId != requestId ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(received)))
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed,
                "业务系统钩子响应的签名或请求 ID 无效。");
        }

        try
        {
            using var document = JsonDocument.Parse(response.BodyBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("钩子响应不是 JSON 对象。");
            }

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed,
                "业务系统钩子响应不是有效的 JSON 对象。");
        }
    }
}
