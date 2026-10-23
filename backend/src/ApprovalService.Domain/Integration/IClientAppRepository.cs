namespace ApprovalService.Domain.Integration;

/// <summary>
/// 接入应用的持久化查询与密钥更新接口。
/// </summary>
public interface IClientAppRepository
{
    /// <summary>
    /// 读取管理列表需要的应用摘要；实现不得装载密钥列。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>不含密钥的接入应用。</returns>
    Task<IReadOnlyList<ClientAppEntity>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 有条件地更新嵌入来源，并在同一事务写入管理员变更记录。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="expectedJson">读取时的原始来源 JSON。</param>
    /// <param name="updatedJson">新的来源 JSON。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="reason">本次操作原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>应用仍处于读取时状态且完成保存时为真。</returns>
    Task<bool> UpdateAllowedEmbedOriginsAsync(string code, string expectedJson, string updatedJson,
        string actorId, string reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default);

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
    /// 管理员创建应用与脱敏审计在同一事务提交。
    /// </summary>
    /// <param name="app">待创建应用。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task CreateWithAuditAsync(ClientAppEntity app, string actorId, string reason, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 轮换 API Key 摘要及签名密钥密文。
    /// </summary>
    /// <param name="app">已登记应用的新密钥状态。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>应用存在且完成轮换为真。</returns>
    Task<bool> UpdateSecretsAsync(ClientAppEntity app, CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理员轮换密钥并在同一事务记录不含密钥的审计摘要。
    /// </summary>
    /// <param name="app">新密钥状态。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>完成轮换为真。</returns>
    Task<bool> UpdateSecretsWithAuditAsync(ClientAppEntity app, string actorId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅在启用状态实际改变时，原子更新状态并递增身份代际。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="enabled">目标启用状态。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>状态确实改变为真。</returns>
    Task<bool> SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理员切换应用状态与脱敏审计在同一事务提交。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="enabled">目标启用状态。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>状态实际改变为真。</returns>
    Task<bool> SetEnabledWithAuditAsync(string code, bool enabled, string actorId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default);
}
