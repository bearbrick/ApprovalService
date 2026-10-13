namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 出错时的响应体，所有开放接口和管理接口都用这一种格式。
/// </summary>
/// <param name="Code">错误码，取值见 <see cref="ErrorCodes"/>。</param>
/// <param name="Message">给人看的中文说明。</param>
/// <param name="Errors">逐项的错误（如哪个字段缺了）；没有时不输出。</param>
/// <param name="TraceId">链路 ID，和响应头 X-Trace-Id 相同。</param>
public sealed record ApiErrorResponse(string Code, string Message, IReadOnlyList<ApiFieldError>? Errors, string? TraceId);

/// <summary>
/// 一项具体的错误。
/// </summary>
/// <param name="Field">出错的字段路径，如 data.items[0].qty；和字段无关时为 null。</param>
/// <param name="Code">错误码。</param>
/// <param name="Message">给人看的中文说明。</param>
public sealed record ApiFieldError(string? Field, string Code, string Message);
