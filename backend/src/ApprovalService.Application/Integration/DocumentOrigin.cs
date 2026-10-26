namespace ApprovalService.Application.Integration;

/// <summary>
/// 检查只能用于原单 iframe 的完整 HTTPS 来源。
/// </summary>
public static class DocumentOrigin
{
    /// <summary>
    /// 仅接受协议、主机和可选端口，不接受路径、用户信息或通配符。
    /// </summary>
    /// <param name="origin">待检查的来源。</param>
    /// <returns>来源符合安全格式时为真。</returns>
    public static bool IsValid(string? origin) =>
        !string.IsNullOrWhiteSpace(origin) && origin.Length <= 256 && !origin.Contains('*') &&
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.UserInfo.Length == 0 &&
        string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}
