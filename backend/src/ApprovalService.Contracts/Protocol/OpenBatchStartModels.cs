namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 同一接入应用的多张审批单发起请求。
/// </summary>
public sealed class OpenBatchStartRequest
{
    /// <summary>
    /// 按原顺序逐张处理的发起请求；单项无效不会回滚其他项。
    /// </summary>
    public List<OpenStartRequest?> Items { get; set; } = [];
}

/// <summary>
/// 批量发起的逐项结果，顺序与请求一致。
/// </summary>
/// <param name="Results">按零起始下标排列的成功审批单或业务错误。</param>
public sealed record OpenBatchStartResponse(IReadOnlyList<OpenBatchStartItemResponse> Results);

/// <summary>
/// 一张审批单的独立发起结果。
/// </summary>
/// <param name="Index">请求列表中的零起始下标。</param>
/// <param name="Instance">成功时的审批单详情。</param>
/// <param name="Error">可修正的业务错误；成功时为空。</param>
public sealed record OpenBatchStartItemResponse(int Index, OpenInstanceResponse? Instance, OpenBatchStartError? Error);

/// <summary>
/// 批量发起中单项可修正的业务错误。
/// </summary>
/// <param name="Code">统一错误码。</param>
/// <param name="Message">中文错误说明。</param>
public sealed record OpenBatchStartError(string Code, string Message);
