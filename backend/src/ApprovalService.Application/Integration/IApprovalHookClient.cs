using System.Text.Json;
using ApprovalService.Domain.Integration;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 事务开始前调用业务系统钩子，并只返回已经验签的响应对象。
/// </summary>
public interface IApprovalHookClient
{
    /// <summary>
    /// 向接入应用登记的完整地址发送 JSON，并核对响应签名和请求 ID。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="url">该应用登记的钩子完整地址。</param>
    /// <param name="request">协议请求体。</param>
    /// <param name="requestId">请求追踪 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已验签的 JSON 对象。</returns>
    Task<JsonElement> InvokeAsync(ClientAppEntity app, string url, object request, string requestId,
        CancellationToken cancellationToken = default);
}
