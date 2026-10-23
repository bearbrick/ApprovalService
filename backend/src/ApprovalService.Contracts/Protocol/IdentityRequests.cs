namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 工号密码登录输入。
/// </summary>
/// <param name="EmployeeNumber">工号。</param>
/// <param name="Password">密码。</param>
public sealed record IdentityLoginRequest(string EmployeeNumber, string Password);

/// <summary>
/// 刷新令牌输入。
/// </summary>
/// <param name="RefreshToken">上次签发的刷新令牌。</param>
public sealed record IdentityRefreshRequest(string RefreshToken);

/// <summary>
/// 修改密码输入。
/// </summary>
/// <param name="CurrentPassword">当前密码。</param>
/// <param name="NewPassword">新密码。</param>
public sealed record IdentityChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// 作废人员全部令牌的管理输入。
/// </summary>
/// <param name="PersonId">内部人员 ID。</param>
public sealed record IdentityRevokeRequest(string PersonId);

/// <summary>
/// 管理员为已有人员开通本地账号的输入。
/// </summary>
/// <param name="PersonId">在职人员 ID。</param>
/// <param name="TemporaryPassword">只用于首次登录的临时密码。</param>
public sealed record IdentityProvisionRequest(string PersonId, string TemporaryPassword);

/// <summary>
/// 登录、刷新或嵌入交换成功后的令牌对。
/// </summary>
/// <param name="PersonId">内部人员 ID。</param>
/// <param name="AccessToken">短期 JWT。</param>
/// <param name="AccessExpiresAt">访问令牌到期时间。</param>
/// <param name="RefreshToken">可轮换刷新令牌。</param>
/// <param name="RefreshExpiresAt">刷新令牌到期时间。</param>
/// <param name="MustChangePassword">首次登录是否必须修改密码。</param>
/// <param name="TargetPath">嵌入交换后允许打开的站内页面；普通登录时为空。</param>
public sealed record IdentityTokensResponse(string PersonId, string AccessToken, DateTimeOffset AccessExpiresAt,
    string RefreshToken, DateTimeOffset RefreshExpiresAt, bool MustChangePassword, string? TargetPath);

/// <summary>
/// 接入应用签发嵌入令牌的请求。
/// </summary>
/// <param name="UserId">该接入应用已绑定的业务系统人员 ID。</param>
/// <param name="TargetPath">限定的站内相对路径。</param>
public sealed record EmbedTokenRequest(string UserId, string TargetPath);

/// <summary>
/// 嵌入令牌签发结果。
/// </summary>
/// <param name="Token">一次性随机令牌。</param>
/// <param name="ExpiresAt">过期时间。</param>
/// <param name="TargetPath">允许打开的站内相对路径。</param>
public sealed record EmbedTokenResponse(string Token, DateTimeOffset ExpiresAt, string TargetPath);

/// <summary>
/// 嵌入令牌交换输入。
/// </summary>
/// <param name="Token">一次性随机令牌。</param>
public sealed record EmbedTokenExchangeRequest(string Token);
