namespace ApprovalService.Domain.Integration;

/// <summary>
/// 为管理用例查询并重排已持久化的回调待发项。
/// </summary>
public interface ICallbackDeliveryRepository
{
    /// <summary>
    /// 查找稳定事件 ID 对应的回调待发项。
    /// </summary>
    /// <param name="eventId">事件 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>存在时为真。</returns>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将未被有效租约持有的回调重新排队，保留事件 ID 与原始正文。
    /// </summary>
    /// <param name="eventId">事件 ID。</param>
    /// <param name="now">本次重发时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已成功重新排队为真。</returns>
    Task<bool> RequeueAsync(string eventId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
