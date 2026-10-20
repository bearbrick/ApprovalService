namespace ApprovalService.Application.Identity;

/// <summary>
/// 本地账号、刷新令牌和业务系统嵌入令牌的应用边界。
/// </summary>
public interface IIdentityAppService
{
    /// <summary>
    /// 用工号和密码登录。
    /// </summary>
    /// <param name="employeeNumber">审批服务工号。</param>
    /// <param name="password">原始密码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>短期访问令牌和刷新令牌。</returns>
    Task<IdentityTokensDto> LoginAsync(string employeeNumber, string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子消费旧刷新令牌并轮换一组新令牌。
    /// </summary>
    /// <param name="refreshToken">原始刷新令牌。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>新的一组令牌。</returns>
    Task<IdentityTokensDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 用户用旧密码修改自己的密码，同时作废全部旧令牌。
    /// </summary>
    /// <param name="personId">当前已认证的人员 ID。</param>
    /// <param name="currentPassword">当前密码。</param>
    /// <param name="newPassword">新密码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task ChangePasswordAsync(string personId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 作废一个人员的全部访问与刷新令牌，供已授权管理员调用。
    /// </summary>
    /// <param name="personId">目标人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task RevokeAllAsync(string personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为已验证的接入应用和人员创建一次性嵌入令牌。
    /// </summary>
    /// <param name="clientAppCode">接入应用编码。</param>
    /// <param name="personId">已绑定的内部人员 ID。</param>
    /// <param name="targetPath">站内相对页面路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>只显示一次的原始令牌。</returns>
    Task<EmbedTokenDto> CreateEmbedTokenAsync(string clientAppCode, string personId, string targetPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子消费一次性嵌入令牌并为绑定人员建立登录会话。
    /// </summary>
    /// <param name="rawToken">原始嵌入令牌。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>新的一组登录令牌及目标页面。</returns>
    Task<IdentityTokensDto> ExchangeEmbedTokenAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// JWT 签名验证后再次核对账号、在职状态及令牌版本。
    /// </summary>
    /// <param name="personId">令牌中的人员 ID。</param>
    /// <param name="tokenVersion">令牌中的版本。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>令牌仍有效为真。</returns>
    Task<bool> ValidateAccessPrincipalAsync(string personId, long tokenVersion,
        CancellationToken cancellationToken = default);
}
