namespace ApprovalService.Domain.Integration;

/// <summary>
/// 管理员重排达到上限的原自动动作事件，不创建第二个事件身份。
/// </summary>
public interface IActionRetryRepository
{
    /// <summary>
    /// 查找属于自动动作的原事件 ID。
    /// </summary>
    /// <param name="eventId">自动动作触发事件 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>存在且正文为自动动作时为真。</returns>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 只将待管理员处理的原事件排入一次人工重试。
    /// </summary>
    /// <param name="eventId">自动动作触发事件 ID。</param>
    /// <param name="now">重新排队时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>成功取得待处理项并重排时为真。</returns>
    Task<bool> RequeueAsync(string eventId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
