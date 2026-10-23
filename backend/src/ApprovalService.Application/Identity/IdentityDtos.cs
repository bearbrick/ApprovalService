namespace ApprovalService.Application.Identity;

/// <summary>
/// 登录、刷新及嵌入交换返回的令牌资料。
/// </summary>
public sealed class IdentityTokensDto
{
    /// <summary>
    /// 审批服务内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// 短期访问令牌。
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// 访问令牌 UTC 过期时间。
    /// </summary>
    public DateTimeOffset AccessExpiresAt
    {
        get; set;
    }

    /// <summary>
    /// 一次性轮换的刷新令牌。
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// 刷新令牌 UTC 过期时间。
    /// </summary>
    public DateTimeOffset RefreshExpiresAt
    {
        get; set;
    }

    /// <summary>
    /// 首次登录是否须改密码。
    /// </summary>
    public bool MustChangePassword
    {
        get; set;
    }

    /// <summary>
    /// 嵌入令牌交换后要打开的站内页面；普通登录时为空。
    /// </summary>
    public string? TargetPath
    {
        get; set;
    }
}

/// <summary>
/// 签发后只显示一次的嵌入令牌。
/// </summary>
public sealed class EmbedTokenDto
{
    /// <summary>
    /// 原始随机令牌。
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// UTC 过期时间。
    /// </summary>
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }

    /// <summary>
    /// 允许打开的站内相对路径。
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;
}
