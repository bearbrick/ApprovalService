namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 错误码，出错时写在错误体的 code 里。调用端 SDK 的 ApprovalErrorCodes.cs 有一份副本，由后端检查程序核对一致（AGENTS.md §6.4）。
/// </summary>
/// <remarks>
/// 骨架阶段只有通用的几个；流程、审批操作、接入相关的错误码在对应任务里追加，追加时同步 docs/contracts.md 和 SDK 的副本。
/// </remarks>
public static class ErrorCodes
{
    /// <summary>
    /// 没带 API Key 或 Key 不对（401）。
    /// </summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>
    /// Key 有效，但不允许做这件事，如用了没授权的流程（403）。
    /// </summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>
    /// 地址不存在，或要找的流程、审批单、任务不存在（404）。
    /// </summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>
    /// 请求体不是合法的 JSON（400）。
    /// </summary>
    public const string InvalidJson = "INVALID_JSON";

    /// <summary>
    /// 请求的数据不符合要求，具体哪个字段写在 errors 里（400）。
    /// </summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>
    /// 和当前状态冲突，如审批单已经结束、别人刚处理过（409）。
    /// </summary>
    public const string Conflict = "CONFLICT";

    /// <summary>
    /// 服务内部错误，属于程序缺陷，带 traceId 反馈给服务方（500）。
    /// </summary>
    public const string InternalError = "INTERNAL_ERROR";
}
