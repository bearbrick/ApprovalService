namespace ApprovalService.Application.Identity;

/// <summary>
/// 会话的可信来源类型，嵌入会话不能获得本地管理员权限。
/// </summary>
public static class IdentitySessionKinds
{
    /// <summary>
    /// 工号与密码直接登录。
    /// </summary>
    public const string Local = "local";

    /// <summary>
    /// 接入应用的一次性嵌入令牌交换。
    /// </summary>
    public const string Embed = "embed";
}

/// <summary>
/// 密码摘要结果。
/// </summary>
public sealed class PasswordDigest
{
    /// <summary>
    /// 密码摘要的 Base64 文本。
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// 随机盐的 Base64 文本。
    /// </summary>
    public string Salt { get; set; } = string.Empty;
}

/// <summary>
/// 可替换的密码摘要边界。
/// </summary>
public interface IIdentityPasswordService
{
    /// <summary>
    /// 为新密码生成随机盐和摘要。
    /// </summary>
    /// <param name="password">原始密码。</param>
    /// <returns>摘要及随机盐。</returns>
    PasswordDigest Hash(string password);

    /// <summary>
    /// 固定时间核对密码摘要。
    /// </summary>
    /// <param name="password">原始密码。</param>
    /// <param name="hash">已保存摘要。</param>
    /// <param name="salt">已保存盐。</param>
    /// <returns>密码一致为真。</returns>
    bool Verify(string password, string hash, string salt);

    /// <summary>
    /// 账号不存在时执行等价摘要计算，降低时间差异。
    /// </summary>
    /// <param name="password">提交的密码。</param>
    /// <returns>始终为假。</returns>
    bool VerifyMissing(string password);
}

/// <summary>
/// 短期访问令牌签发边界；签名校验在接口层完成。
/// </summary>
public interface IIdentityAccessTokenService
{
    /// <summary>
    /// 为账号和令牌版本签发访问令牌。
    /// </summary>
    /// <param name="personId">人员 ID。</param>
    /// <param name="tokenVersion">账号令牌版本。</param>
    /// <param name="mustChangePassword">首次登录是否必须修改密码。</param>
    /// <param name="clientAppCode">嵌入会话的接入应用编码；本地密码登录时为空。</param>
    /// <param name="externalUserId">嵌入会话的业务系统人员 ID；本地密码登录时为空。</param>
    /// <param name="issuedAt">签发时间。</param>
    /// <param name="expiresAt">过期时间。</param>
    /// <returns>已签名的 JWT。</returns>
    string Issue(string personId, long tokenVersion, bool mustChangePassword, string? clientAppCode,
        string? externalUserId,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt);
}
