using ApprovalService.Domain.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 周期领取数据库中的待发项和定时任务，并在租约之外调用实际处理器。
/// </summary>
public sealed class DurableJobWorker : BackgroundService
{
    /// <summary>
    /// 单项执行的最大等待时间，小于租约时长。
    /// </summary>
    private static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 租约时长；超时或进程退出后可由其他实例重领。
    /// </summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 无到期任务时的查询间隔。
    /// </summary>
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 待发队列。
    /// </summary>
    private readonly OutboxQueue outbox;

    /// <summary>
    /// 定时任务队列。
    /// </summary>
    private readonly ScheduledJobQueue scheduledJobs;

    /// <summary>
    /// 为每项处理建立独立依赖范围。
    /// </summary>
    private readonly IServiceScopeFactory scopeFactory;

    /// <summary>
    /// 可替换的时钟，便于确定性检查。
    /// </summary>
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// 创建后台执行器。
    /// </summary>
    /// <param name="outbox">待发队列。</param>
    /// <param name="scheduledJobs">定时任务队列。</param>
    /// <param name="scopeFactory">依赖范围工厂。</param>
    /// <param name="timeProvider">时钟；未注册时使用系统时钟。</param>
    public DurableJobWorker(OutboxQueue outbox, ScheduledJobQueue scheduledJobs,
        IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
    {
        this.outbox = outbox;
        this.scheduledJobs = scheduledJobs;
        this.scopeFactory = scopeFactory;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await RunOnceAsync(timeProvider.GetUtcNow(), stoppingToken);
            if (processed == 0)
            {
                await Task.Delay(IdleInterval, timeProvider, stoppingToken);
            }
        }
    }

    /// <summary>
    /// 各处理一项到期的待发项和定时任务；便于固定时间检查和宿主主动唤醒。
    /// </summary>
    /// <param name="now">本轮领取的固定 UTC 时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本轮领取的项数，零表示当前没有到期工作。</returns>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var processed = 0;
        var outboxItem = await outbox.ClaimNextAsync(now, LeaseDuration, cancellationToken);
        if (outboxItem is not null)
        {
            await DeliverAsync(outboxItem, now, cancellationToken);
            processed++;
        }

        var job = await scheduledJobs.ClaimNextAsync(now, LeaseDuration, cancellationToken);
        if (job is not null)
        {
            await ExecuteJobAsync(job, now, cancellationToken);
            processed++;
        }

        return processed;
    }

    /// <summary>
    /// 在数据库事务之外发送一项待发消息。
    /// </summary>
    /// <param name="item">已领取的待发项。</param>
    /// <param name="now">本轮固定时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    private async Task DeliverAsync(OutboxItemEntity item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            var handlers = scope.ServiceProvider.GetServices<IOutboxHandler>()
                .Where(candidate => candidate.Kind == item.Kind).ToArray();
            if (handlers.Length != 1)
            {
                throw new InvalidOperationException("待发项处理器缺失或编码重复。");
            }

            using var timeout = new CancellationTokenSource(HandlerTimeout, timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await handlers[0].DeliverAsync(item, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 宿主停止时保留租约，超时后由新进程接续处理。
            throw;
        }
        catch (Exception error)
        {
            await outbox.FailAsync(item, now, error.GetType().Name, cancellationToken);
            return;
        }

        // 成功发送后的确认单独执行；数据库故障由宿主报告，租约到期后仍可重试。
        await outbox.CompleteAsync(item, now, cancellationToken);
    }

    /// <summary>
    /// 在数据库事务之外执行一项持久化定时任务。
    /// </summary>
    /// <param name="job">已领取任务。</param>
    /// <param name="now">本轮固定时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    private async Task ExecuteJobAsync(ScheduledJobEntity job, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            var handlers = scope.ServiceProvider.GetServices<IScheduledJobHandler>()
                .Where(candidate => candidate.Kind == job.Kind).ToArray();
            if (handlers.Length != 1)
            {
                throw new InvalidOperationException("定时任务处理器缺失或编码重复。");
            }

            using var timeout = new CancellationTokenSource(HandlerTimeout, timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await handlers[0].ExecuteAsync(job, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await scheduledJobs.FailAsync(job, now, cancellationToken);
            return;
        }

        await scheduledJobs.CompleteAsync(job, now, cancellationToken);
    }
}
