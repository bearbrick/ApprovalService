namespace ApprovalService.Client;

/// <summary>
/// 调用审批流服务出错：服务返回了错误，或者连不上、超时。
/// </summary>
public sealed class ApprovalServiceException : Exception
{
    /// <summary>
    /// 创建异常。
    /// </summary>
    /// <param name="errorCode">错误码，取值见 <see cref="ApprovalErrorCodes"/>。</param>
    /// <param name="message">中文说明。</param>
    /// <param name="statusCode">HTTP 状态码；没收到响应时为 null。</param>
    /// <param name="traceId">服务返回的链路 ID；没有时为 null。</param>
    /// <param name="innerException">引起这个异常的异常；没有时为 null。</param>
    public ApprovalServiceException(string errorCode, string message, int? statusCode = null, string? traceId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        TraceId = traceId;
    }

    /// <summary>
    /// 错误码。
    /// </summary>
    public string ErrorCode
    {
        get;
    }

    /// <summary>
    /// HTTP 状态码；没收到响应（连不上、超时）时为 null。
    /// </summary>
    public int? StatusCode
    {
        get;
    }

    /// <summary>
    /// 服务返回的链路 ID，反馈问题时提供；没有时为 null。
    /// </summary>
    public string? TraceId
    {
        get;
    }
}
