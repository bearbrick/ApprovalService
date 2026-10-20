using ApprovalService.Domain.Runtime;

namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 一种待发项的外部发送实现；外部调用必须以事件 ID 实现幂等。
/// </summary>
public interface IOutboxHandler
{
    /// <summary>
    /// 可处理的待发项种类。
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// 执行外部发送；数据库租约之外调用，不占用审批事务。
    /// </summary>
    /// <param name="item">本次领取的待发项。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken);
}
