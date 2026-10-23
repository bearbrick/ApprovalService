namespace ApprovalService.Domain.Integration;

/// <summary>
/// 接入应用的持久化查询与密钥更新接口。
/// </summary>
public interface IClientAppRepository
{
    /// <summary>
    /// 按编码读取接入应用。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>应用；不存在时为空。</returns>
    Task<ClientAppEntity?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 API Key 摘要读取启用的接入应用。
    /// </summary>
    /// <param name="apiKeyHash">已计算的 SHA-256 摘要。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>启用的应用；未匹配时为空。</returns>
    Task<ClientAppEntity?> FindActiveByKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存首次登记的接入应用。
    /// </summary>
    /// <param name="app">待保存应用。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task CreateAsync(ClientAppEntity app, CancellationToken cancellationToken = default);

    /// <summary>
    /// 轮换 API Key 摘要及签名密钥密文。
    /// </summary>
    /// <param name="app">已登记应用的新密钥状态。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>应用存在且完成轮换为真。</returns>
    Task<bool> UpdateSecretsAsync(ClientAppEntity app, CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅在启用状态实际改变时，原子更新状态并递增身份代际。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="enabled">目标启用状态。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>状态确实改变为真。</returns>
    Task<bool> SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken = default);
}
