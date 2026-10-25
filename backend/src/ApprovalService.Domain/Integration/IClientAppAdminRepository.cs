namespace ApprovalService.Domain.Integration;

/// <summary>
/// 接入应用管理员名单的持久化与可信权限检查边界。
/// </summary>
public interface IClientAppAdminRepository
{
    /// <summary>
    /// 读取应用管理员名单，不加载应用密钥。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>名单 JSON；应用不存在时为空。</returns>
    Task<string?> GetAdministratorIdsJsonAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在串行化事务中验证人员并按预期名单替换，且记录审计。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="expectedJson">预期旧名单 JSON。</param>
    /// <param name="updatedJson">新名单 JSON。</param>
    /// <param name="personIds">新名单中的内部人员 ID。</param>
    /// <param name="actorId">组织管理员 ID。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>写入结果。</returns>
    Task<ClientAppAdminWriteResult> ReplaceAsync(string code, string expectedJson, string updatedJson,
        IReadOnlyList<string> personIds, string actorId, string reason, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查已启用应用的管理员名单是否包含在职人员。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>成员资格有效时为真。</returns>
    Task<bool> IsActiveAdministratorAsync(string code, string personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查应用是否启用。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>应用存在且启用时为真。</returns>
    Task<bool> IsAppEnabledAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>
/// 管理员名单条件写入的结果。
/// </summary>
public enum ClientAppAdminWriteResult
{
    /// <summary>
    /// 写入成功。
    /// </summary>
    Updated,
    /// <summary>
    /// 应用不存在。
    /// </summary>
    AppNotFound,
    /// <summary>
    /// 旧名单已变化。
    /// </summary>
    Conflict,
    /// <summary>
    /// 名单含不存在或停用的人员。
    /// </summary>
    InvalidPerson,
}
