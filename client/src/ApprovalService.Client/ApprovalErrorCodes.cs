namespace ApprovalService.Client;

/// <summary>
/// 错误码，<see cref="ApprovalServiceException.ErrorCode"/> 的取值。服务端的错误码是后端 Contracts 里 ErrorCodes 的副本（名字和值都要一样，
/// 由后端检查程序核对）；<see cref="NetworkError"/>、<see cref="Timeout"/> 是 SDK 自己的。
/// </summary>
public static class ApprovalErrorCodes
{
    /// <summary>
    /// 没带 API Key 或 Key 不对。
    /// </summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>
    /// Key 有效，但不允许做这件事。
    /// </summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>
    /// 地址、流程、审批单或任务不存在。
    /// </summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>
    /// 请求体不是合法的 JSON。
    /// </summary>
    public const string InvalidJson = "INVALID_JSON";

    /// <summary>
    /// 请求的数据不符合要求，具体哪个字段写在异常的说明里。
    /// </summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>
    /// 业务系统钩子超时、不可用或响应无效。
    /// </summary>
    public const string BusinessHookFailed = "BUSINESS_HOOK_FAILED";

    /// <summary>
    /// 和当前状态冲突，如审批单已经结束。
    /// </summary>
    public const string Conflict = "CONFLICT";

    /// <summary>
    /// 服务内部错误，带 <see cref="ApprovalServiceException.TraceId"/> 反馈给服务方。
    /// </summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>
    /// SDK 自己的：连不上服务（地址不对、网络不通、服务没启动）。
    /// </summary>
    public const string NetworkError = "NETWORK_ERROR";

    /// <summary>
    /// SDK 自己的：等待响应超时（<see cref="ApprovalServiceClientOptions.Timeout"/>）。
    /// </summary>
    public const string Timeout = "TIMEOUT";
}
