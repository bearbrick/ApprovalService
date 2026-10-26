namespace ApprovalService.Domain.Administration;

/// <summary>
/// 在同一事务中保存系统信息和脱敏管理员审计。
/// </summary>
public interface ISystemBrandingRepository
{
    /// <summary>
    /// 读取当前全局信息。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已保存的信息；空库时为空。</returns>
    Task<SystemBrandingEntity?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按预期修订原子保存并追加审计。
    /// </summary>
    /// <param name="value">经过校验的新信息。</param>
    /// <param name="expectedRevision">页面读取的修订号。</param>
    /// <param name="actorId">已登录管理员的内部人员 ID。</param>
    /// <param name="reason">明确填写的原因。</param>
    /// <param name="action">名称或 Logo 变更类别。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>成功保存时为真；修订冲突时为假。</returns>
    Task<bool> SaveAsync(SystemBrandingEntity value, long expectedRevision, string actorId,
        string reason, string action, CancellationToken cancellationToken = default);
}
