using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 待发队列的原子领取、完成和失败重试操作。
/// </summary>
public sealed class OutboxQueue
{
    /// <summary>
    /// 每次领取最多探查的候选数，避免一个并发争用项阻塞全部发送。
    /// </summary>
    private const int CandidateLimit = 32;

    /// <summary>
    /// 为每次数据库操作创建独立上下文。
    /// </summary>
    private readonly IDbContextFactory<ApprovalDbContext> contextFactory;

    /// <summary>
    /// 创建待发队列访问器。
    /// </summary>
    /// <param name="contextFactory">数据库上下文工厂。</param>
    public OutboxQueue(IDbContextFactory<ApprovalDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    /// <summary>
    /// 以数据库条件更新领取一项到期工作；空结果表示暂时没有可执行项。
    /// </summary>
    /// <param name="now">本次查询的固定 UTC 时间。</param>
    /// <param name="leaseDuration">正数租约时长。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>含独占租约标记的待发项，或空值。</returns>
    public async Task<OutboxItemEntity?> ClaimNextAsync(DateTimeOffset now, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await context.OutboxItems.AsNoTracking()
            .Where(item => item.AvailableAt <= now &&
                (item.Status == JobStatuses.Pending ||
                    item.Status == JobStatuses.Leased && item.LeaseUntil <= now))
            .OrderBy(item => item.AvailableAt).ThenBy(item => item.Id)
            .Select(item => item.Id).Take(CandidateLimit).ToArrayAsync(cancellationToken);
        foreach (var id in candidates)
        {
            // 条件更新是跨实例的抢锁点；只有一个执行者能把同一行改成新租约。
            var leaseOwner = Guid.NewGuid().ToString("N");
            var claimed = await context.OutboxItems.Where(item => item.Id == id && item.AvailableAt <= now &&
                    (item.Status == JobStatuses.Pending ||
                        item.Status == JobStatuses.Leased && item.LeaseUntil <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, JobStatuses.Leased)
                    .SetProperty(item => item.LeaseOwner, leaseOwner)
                    .SetProperty(item => item.LeaseUntil, now.Add(leaseDuration))
                    .SetProperty(item => item.Attempts, item => item.Attempts + 1)
                    .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
            if (claimed == 1)
            {
                return await context.OutboxItems.AsNoTracking().SingleAsync(item => item.Id == id, cancellationToken);
            }
        }

        return null;
    }

    /// <summary>
    /// 只有当前租约仍有效时才能确认已发送。
    /// </summary>
    /// <param name="claim">领取时返回的项及租约标记。</param>
    /// <param name="now">确认时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>是否成功确认。</returns>
    public async Task<bool> CompleteAsync(OutboxItemEntity claim, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await context.OutboxItems.Where(item => item.Id == claim.Id &&
                item.Status == JobStatuses.Leased && item.LeaseOwner == claim.LeaseOwner &&
                item.LeaseUntil > now && item.Version == claim.Version)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, JobStatuses.Completed)
                .SetProperty(item => item.CompletedAt, now)
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        return updated == 1;
    }

    /// <summary>
    /// 记录失败并按固定退避表重新排队；已过期的旧租约不能覆盖新执行者。
    /// </summary>
    /// <param name="claim">领取时返回的项及租约标记。</param>
    /// <param name="now">失败时间。</param>
    /// <param name="errorCode">不含敏感信息的短错误摘要。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>是否成功安排重试。</returns>
    public async Task<bool> FailAsync(OutboxItemEntity claim, DateTimeOffset now, string errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var nextAttempt = now.Add(JobRetrySchedule.AfterFailure(claim.Attempts));
        var safeErrorCode = errorCode.Length > 200 ? errorCode[..200] : errorCode;
        var updated = await context.OutboxItems.Where(item => item.Id == claim.Id &&
                item.Status == JobStatuses.Leased && item.LeaseOwner == claim.LeaseOwner &&
                item.LeaseUntil > now && item.Version == claim.Version)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, JobStatuses.Pending)
                .SetProperty(item => item.AvailableAt, nextAttempt)
                .SetProperty(item => item.LastError, safeErrorCode)
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        return updated == 1;
    }
}
