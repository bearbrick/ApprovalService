namespace ApprovalService.Client.Internal;

/// <summary>
/// HTTP 头，是后端 Contracts 里 HeaderNames 的副本：名字和值都要一样，由后端检查程序核对（AGENTS.md §6.4）。
/// </summary>
internal static class Protocol
{
    /// <summary>
    /// 接入应用的 API Key。
    /// </summary>
    public const string ApiKey = "X-Api-Key";

    /// <summary>
    /// SDK 每次请求生成的请求 ID，服务原样返回并记进日志。
    /// </summary>
    public const string RequestId = "X-Request-Id";

    /// <summary>
    /// 服务返回的链路 ID，反馈问题时提供。
    /// </summary>
    public const string TraceId = "X-Trace-Id";

    /// <summary>
    /// 回调请求体的签名（sha256=十六进制小写）。
    /// </summary>
    public const string Signature = "X-Approval-Signature";
}
