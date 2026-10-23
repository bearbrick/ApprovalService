namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 已验证嵌入会话的来源范围，供前端限定完成消息的目标来源。
/// </summary>
/// <param name="ClientAppCode">访问令牌绑定的接入应用编码。</param>
/// <param name="AllowedOrigins">该应用当前登记的完整网站来源。</param>
public sealed record CenterEmbedContextResponse(string ClientAppCode, IReadOnlyList<string> AllowedOrigins);
