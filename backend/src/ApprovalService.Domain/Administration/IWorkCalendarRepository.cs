namespace ApprovalService.Domain.Administration;

/// <summary>
/// 全局工作日历快照及原子管理审计持久化。
/// </summary>
public interface IWorkCalendarRepository
{
    /// <summary>
    /// 一次读取整个日历修订。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>在线日历；尚未保存时为空。</returns>
    Task<WorkCalendarEntity?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据旧修订原子保存新日历与审计。
    /// </summary>
    /// <param name="value">经过校验的新日历。</param>
    /// <param name="expectedRevision">预期修订。</param>
    /// <param name="actorId">真实管理员 ID。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>成功为真，并发冲突为假。</returns>
    Task<bool> SaveAsync(WorkCalendarEntity value, long expectedRevision, string actorId,
        string reason, CancellationToken cancellationToken = default);
}
