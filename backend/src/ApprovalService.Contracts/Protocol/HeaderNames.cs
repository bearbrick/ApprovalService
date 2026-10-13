namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 开放接口用到的 HTTP 头。调用端 SDK 的 Internal/Protocol.cs 有一份副本，由后端检查程序核对一致（AGENTS.md §6.4）。
/// </summary>
public static class HeaderNames
{
    /// <summary>
    /// 接入应用的 API Key。
    /// </summary>
    public const string ApiKey = "X-Api-Key";

    /// <summary>
    /// 调用方生成的请求 ID，原样返回并记进日志；没传时服务生成一个。
    /// </summary>
    public const string RequestId = "X-Request-Id";

    /// <summary>
    /// 本次请求的链路 ID，出错时同样写在错误体的 traceId 里，反馈问题时提供。
    /// </summary>
    public const string TraceId = "X-Trace-Id";

    /// <summary>
    /// 回调请求体的签名：sha256=请求体用接入应用的签名密钥做 HMAC-SHA256 的十六进制小写。
    /// </summary>
    public const string Signature = "X-Approval-Signature";
}
