namespace ApprovalService.Domain.Identity;

/// <summary>
/// 绑定人员的本地账号；登录层可换成统一身份提供者。
/// </summary>
public sealed class LocalAccountEntity
{
    /// <summary>人员 ID。</summary>
    public string PersonId { get; set; } = string.Empty;
    /// <summary>PBKDF2 密码摘要，Base64 编码。</summary>
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>随机盐，Base64 编码。</summary>
    public string PasswordSalt { get; set; } = string.Empty;
    /// <summary>首次登录是否必须修改密码。</summary>
    public bool MustChangePassword { get; set; } = true;
    /// <summary>作废旧令牌用的版本。</summary>
    public long TokenVersion { get; set; }
    /// <summary>账号是否启用。</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 仅保存摘要的可轮换刷新令牌。
/// </summary>
public sealed class RefreshTokenEntity
{
    /// <summary>随机令牌的 SHA-256 摘要。</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>所属人员 ID。</summary>
    public string PersonId { get; set; } = string.Empty;
    /// <summary>签发时的令牌版本。</summary>
    public long TokenVersion { get; set; }
    /// <summary>过期时间。</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>已消费时间，非空表示不可再使用。</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}

/// <summary>
/// 业务系统为指定人员签发的一次性嵌入令牌。
/// </summary>
public sealed class EmbedTokenEntity
{
    /// <summary>随机令牌的 SHA-256 摘要。</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>接入应用编码。</summary>
    public string ClientAppCode { get; set; } = string.Empty;
    /// <summary>绑定的人员 ID。</summary>
    public string PersonId { get; set; } = string.Empty;
    /// <summary>目标相对页面路径。</summary>
    public string TargetPath { get; set; } = string.Empty;
    /// <summary>过期时间。</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>消费时间。</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}
