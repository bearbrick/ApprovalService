using ApprovalService.Domain.Runtime;

namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 一种持久化定时任务的处理实现。
/// </summary>
public interface IScheduledJobHandler
{
    /// <summary>
    /// 可处理的定时任务种类。
    /// </summary>
    string Kind
    {
        get;
    }

    /// <summary>
    /// 执行到期任务；重启或租约过期后可能重试，处理逻辑应幂等。
    /// </summary>
    /// <param name="job">本次领取的任务。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task ExecuteAsync(ScheduledJobEntity job, CancellationToken cancellationToken);
}
