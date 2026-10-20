using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 持久化定时任务的原子领取与执行结果记录。
/// </summary>
public sealed class ScheduledJobQueue
{
    /// <summary>
    /// 一次探查的候选上限。
    /// </summary>
    private const int CandidateLimit = 32;

    /// <summary>
    /// 数据库上下文工厂。
    /// </summary>
    private readonly IDbContextFactory<ApprovalDbContext> contextFactory;

    /// <summary>
    /// 创建定时任务队列访问器。
    /// </summary>
    /// <param name="contextFactory">数据库上下文工厂。</param>
    public ScheduledJobQueue(IDbContextFactory<ApprovalDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    /// <summary>
    /// 原子领取一项已到期任务；执行中意外停机的任务在租约到期后可再次领取。
    /// </summary>
    /// <param name="now">固定 UTC 时间。</param>
    /// <param name="leaseDuration">正数租约时长。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已领取任务，或空值。</returns>
    public async Task<ScheduledJobEntity?> ClaimNextAsync(DateTimeOffset now, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await context.ScheduledJobs.AsNoTracking()
            .Where(job => job.DueAt <= now &&
                (job.Status == JobStatuses.Pending ||
                    job.Status == JobStatuses.Leased && job.LeaseUntil <= now))
            .OrderBy(job => job.DueAt).ThenBy(job => job.Id)
            .Select(job => job.Id).Take(CandidateLimit).ToArrayAsync(cancellationToken);
        foreach (var id in candidates)
        {
            var leaseOwner = Guid.NewGuid().ToString("N");
            var claimed = await context.ScheduledJobs.Where(job => job.Id == id && job.DueAt <= now &&
                    (job.Status == JobStatuses.Pending ||
                        job.Status == JobStatuses.Leased && job.LeaseUntil <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(job => job.Status, JobStatuses.Leased)
                    .SetProperty(job => job.LeaseOwner, leaseOwner)
                    .SetProperty(job => job.LeaseUntil, now.Add(leaseDuration))
                    .SetProperty(job => job.Attempts, job => job.Attempts + 1)
                    .SetProperty(job => job.Version, job => job.Version + 1), cancellationToken);
            if (claimed == 1)
            {
                return await context.ScheduledJobs.AsNoTracking().SingleAsync(job => job.Id == id, cancellationToken);
            }
        }

        return null;
    }

    /// <summary>
    /// 标记当前租约的任务已成功执行。
    /// </summary>
    /// <param name="claim">已领取任务。</param>
    /// <param name="now">确认时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>是否成功确认。</returns>
    public async Task<bool> CompleteAsync(ScheduledJobEntity claim, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await context.ScheduledJobs.Where(job => job.Id == claim.Id &&
                job.Status == JobStatuses.Leased && job.LeaseOwner == claim.LeaseOwner &&
                job.LeaseUntil > now && job.Version == claim.Version)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, JobStatuses.Completed)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(job => job.Version, job => job.Version + 1), cancellationToken);
        return updated == 1;
    }

    /// <summary>
    /// 将失败任务重新安排到退避后的时间，避免重启后丢失。
    /// </summary>
    /// <param name="claim">已领取任务。</param>
    /// <param name="now">失败时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>是否成功重新排队。</returns>
    public async Task<bool> FailAsync(ScheduledJobEntity claim, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var dueAt = now.Add(JobRetrySchedule.AfterFailure(claim.Attempts));
        var updated = await context.ScheduledJobs.Where(job => job.Id == claim.Id &&
                job.Status == JobStatuses.Leased && job.LeaseOwner == claim.LeaseOwner &&
                job.LeaseUntil > now && job.Version == claim.Version)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, JobStatuses.Pending)
                .SetProperty(job => job.DueAt, dueAt)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(job => job.Version, job => job.Version + 1), cancellationToken);
        return updated == 1;
    }
}
