using ApprovalService.Domain.Organization;

namespace ApprovalService.Domain.Identity;

/// <summary>
/// 本地账号和一次性令牌的持久化边界。
/// </summary>
public interface IIdentityRepository
{
    /// <summary>
    /// 用工号读取在职状态与账号。
    /// </summary>
    /// <param name="employeeNumber">工号。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>人员与账号；不存在时为空。</returns>
    Task<IdentityAccountSnapshot?> FindByEmployeeNumberAsync(string employeeNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 用内部人员 ID 读取在职状态与账号。
    /// </summary>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>人员与账号；不存在时为空。</returns>
    Task<IdentityAccountSnapshot?> FindByPersonIdAsync(string personId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为已有人员开通账号。
    /// </summary>
    /// <param name="account">含初始密码摘要的新账号。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task CreateAccountAsync(LocalAccountEntity account, CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改密码并递增令牌版本；版本不一致时不写入。
    /// </summary>
    /// <param name="personId">人员 ID。</param>
    /// <param name="expectedVersion">读取时的令牌版本。</param>
    /// <param name="passwordHash">新的密码摘要。</param>
    /// <param name="passwordSalt">新的随机盐。</param>
    /// <param name="mustChangePassword">下次登录是否仍须改密码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>修改成功为真。</returns>
    Task<bool> ChangePasswordAsync(string personId, long expectedVersion, string passwordHash,
        string passwordSalt, bool mustChangePassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子累计一次密码错误，到阈值后设置短期锁定。
    /// </summary>
    /// <param name="personId">人员 ID。</param>
    /// <param name="now">失败时间。</param>
    /// <param name="maxAttempts">锁定阈值。</param>
    /// <param name="lockoutDuration">锁定时长。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>本次失败后的锁定截止时间；未锁定时为空。</returns>
    Task<DateTimeOffset?> RecordFailedLoginAsync(string personId, DateTimeOffset now, int maxAttempts,
        TimeSpan lockoutDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 密码校验成功后清除连续失败次数；并发新锁定不可被旧登录清除。
    /// </summary>
    /// <param name="personId">人员 ID。</param>
    /// <param name="now">本次登录的时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>账号仍未锁定且清除成功时为真。</returns>
    Task<bool> ResetFailedLoginsAsync(string personId, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 递增人员的令牌版本，使所有旧访问与刷新令牌失效。
    /// </summary>
    /// <param name="personId">人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>递增后的版本；账号不存在时为空。</returns>
    Task<long?> RevokeAllTokensAsync(string personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存仅含摘要的刷新令牌。
    /// </summary>
    /// <param name="token">刷新令牌记录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task AddRefreshTokenAsync(RefreshTokenEntity token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子消费未过期的刷新令牌；并发请求至多一个成功。
    /// </summary>
    /// <param name="tokenHash">提交令牌的摘要。</param>
    /// <param name="now">当前时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>成功消费的记录；否则为空。</returns>
    Task<RefreshTokenEntity?> ConsumeRefreshTokenAsync(string tokenHash, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存仅含摘要的一次性嵌入令牌。
    /// </summary>
    /// <param name="token">嵌入令牌记录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task AddEmbedTokenAsync(EmbedTokenEntity token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子消费未过期的嵌入令牌；并发请求至多一个成功。
    /// </summary>
    /// <param name="tokenHash">提交令牌的摘要。</param>
    /// <param name="now">当前时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>成功消费的记录；否则为空。</returns>
    Task<EmbedTokenEntity?> ConsumeEmbedTokenAsync(string tokenHash, DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 账号和人员在职状态的一次读取结果。
/// </summary>
public sealed class IdentityAccountSnapshot
{
    /// <summary>
    /// 人员主数据。
    /// </summary>
    public PersonEntity Person { get; set; } = new();

    /// <summary>
    /// 本地账号；仅有人员而尚未开通账号时为空。
    /// </summary>
    public LocalAccountEntity? Account { get; set; }
}
