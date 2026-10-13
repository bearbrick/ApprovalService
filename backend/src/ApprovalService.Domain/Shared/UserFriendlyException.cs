namespace ApprovalService.Domain.Shared;

/// <summary>
/// 调用方或使用者能自己修正的问题（数据不对、状态不允许、没有权限……）。接口层按错误码转成 400、403、404、409，消息原样给人看。
/// 程序缺陷不要包装成它，让它以 INTERNAL_ERROR 暴露（AGENTS.md R6）。
/// </summary>
/// <param name="code">错误码，取值见 Contracts 的 ErrorCodes。</param>
/// <param name="message">给人看的中文说明。</param>
public sealed class UserFriendlyException(string code, string message) : Exception(message)
{
    /// <summary>
    /// 错误码。
    /// </summary>
    public string Code { get; } = code;
}
