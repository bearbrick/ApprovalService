using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用真实 SQLite 文件检查待发队列和定时任务的租约、重试与重启恢复。
/// </summary>
internal static class JobChecks
{
    /// <summary>
    /// 运行持久任务的场景检查。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        check("待发失败按一分、五分、三十分、两小时退避并封顶一天",
            JobRetrySchedule.AfterFailure(1) == TimeSpan.FromMinutes(1) &&
            JobRetrySchedule.AfterFailure(2) == TimeSpan.FromMinutes(5) &&
            JobRetrySchedule.AfterFailure(3) == TimeSpan.FromMinutes(30) &&
            JobRetrySchedule.AfterFailure(4) == TimeSpan.FromHours(2) &&
            JobRetrySchedule.AfterFailure(20) == TimeSpan.FromDays(1));
        var directory = Path.Combine(Path.GetTempPath(), "approval-jobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "jobs.db");
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + databasePath + ";Default Timeout=10")
                .Options;
            await using (var context = new ApprovalDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
            }

            await CheckOutboxAsync(options, check);
            await CheckScheduledJobsAsync(options, check);
            await CheckWorkerAsync(options, check);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 检查待发队列的跨实例抢租约、租约回收和退避。
    /// </summary>
    /// <param name="options">同一数据库的连接选项。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckOutboxAsync(DbContextOptions<ApprovalDbContext> options,
        Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
        await using (var context = new ApprovalDbContext(options))
        {
            context.OutboxItems.Add(new OutboxItemEntity
            {
                Id = "outbox-one",
                InstanceId = "instance",
                EventId = "event-one",
                Kind = "callback",
                PayloadJson = "{}",
                AvailableAt = now,
            });
            await context.SaveChangesAsync();
        }

        var factory = new PooledDbContextFactory<ApprovalDbContext>(options);
        var firstQueue = new OutboxQueue(factory);
        var secondQueue = new OutboxQueue(factory);
        var claims = await Task.WhenAll(
            firstQueue.ClaimNextAsync(now, TimeSpan.FromMinutes(2)),
            secondQueue.ClaimNextAsync(now, TimeSpan.FromMinutes(2)));
        check("两个实例并发抢同一待发项只有一个取得租约", claims.Count(item => item is not null) == 1);
        var original = claims.Single(item => item is not null)!;
        check("租约有效期间其他实例不能重复领取", await secondQueue.ClaimNextAsync(
            now.AddMinutes(1), TimeSpan.FromMinutes(2)) is null);
        var recovered = await secondQueue.ClaimNextAsync(now.AddMinutes(3), TimeSpan.FromMinutes(2));
        check("待发项租约过期后可由新实例恢复", recovered is not null &&
            recovered.LeaseOwner != original.LeaseOwner);
        check("旧执行者不能在新租约之后确认发送", !await firstQueue.CompleteAsync(original,
            now.AddMinutes(3)));
        check("重领后的第二次失败进入五分钟退避", await secondQueue.FailAsync(recovered!, now.AddMinutes(3),
            "TestFailure") && await secondQueue.ClaimNextAsync(now.AddMinutes(7).AddSeconds(30),
            TimeSpan.FromMinutes(2)) is null);
        var retry = await firstQueue.ClaimNextAsync(now.AddMinutes(8), TimeSpan.FromMinutes(2));
        check("重试时间到达后待发项仍在数据库", retry is not null && retry.Attempts == 3);
        check("重试完成后不再领取且记录完成时间", await firstQueue.CompleteAsync(retry!, now.AddMinutes(8)) &&
            await secondQueue.ClaimNextAsync(now.AddMinutes(9), TimeSpan.FromMinutes(2)) is null);
    }

    /// <summary>
    /// 检查定时任务在重建队列实例后仍可领取和重试。
    /// </summary>
    /// <param name="options">同一数据库的连接选项。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckScheduledJobsAsync(DbContextOptions<ApprovalDbContext> options,
        Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        await using (var context = new ApprovalDbContext(options))
        {
            context.ScheduledJobs.Add(new ScheduledJobEntity
            {
                Id = "scheduled-one",
                Kind = "reminder",
                PayloadJson = "{}",
                DueAt = now,
            });
            await context.SaveChangesAsync();
        }

        var factory = new PooledDbContextFactory<ApprovalDbContext>(options);
        var beforeRestart = new ScheduledJobQueue(factory);
        var claim = await beforeRestart.ClaimNextAsync(now, TimeSpan.FromMinutes(2));
        var afterRestart = new ScheduledJobQueue(new PooledDbContextFactory<ApprovalDbContext>(options));
        check("重建队列实例后定时任务租约仍存在", claim is not null &&
            await afterRestart.ClaimNextAsync(now.AddMinutes(1), TimeSpan.FromMinutes(2)) is null);
        var recovered = await afterRestart.ClaimNextAsync(now.AddMinutes(3), TimeSpan.FromMinutes(2));
        check("服务重启后过期的定时任务能继续执行", recovered is not null &&
            recovered.LeaseOwner != claim!.LeaseOwner);
        check("定时任务失败后按退避重新排期", await afterRestart.FailAsync(recovered!, now.AddMinutes(3)) &&
            await afterRestart.ClaimNextAsync(now.AddMinutes(7).AddSeconds(30), TimeSpan.FromMinutes(2)) is null);
        var retry = await beforeRestart.ClaimNextAsync(now.AddMinutes(8), TimeSpan.FromMinutes(2));
        check("定时任务重试后可完成", retry is not null &&
            await beforeRestart.CompleteAsync(retry, now.AddMinutes(8)));
    }

    /// <summary>
    /// 检查后台执行器实际调用两类处理器并持久确认结果。
    /// </summary>
    /// <param name="options">同一数据库的连接选项。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckWorkerAsync(DbContextOptions<ApprovalDbContext> options,
        Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
        await using (var context = new ApprovalDbContext(options))
        {
            context.OutboxItems.Add(new OutboxItemEntity
            {
                Id = "outbox-worker",
                InstanceId = "instance",
                EventId = "event-worker",
                Kind = "callback",
                PayloadJson = "{}",
                AvailableAt = now,
            });
            context.ScheduledJobs.Add(new ScheduledJobEntity
            {
                Id = "scheduled-worker",
                Kind = "reminder",
                PayloadJson = "{}",
                DueAt = now,
            });
            await context.SaveChangesAsync();
        }

        var outboxHandler = new RecordingOutboxHandler();
        var jobHandler = new RecordingScheduledJobHandler();
        using var services = new ServiceCollection()
            .AddSingleton<IOutboxHandler>(outboxHandler)
            .AddSingleton<IScheduledJobHandler>(jobHandler)
            .BuildServiceProvider();
        var factory = new PooledDbContextFactory<ApprovalDbContext>(options);
        var worker = new DurableJobWorker(new OutboxQueue(factory), new ScheduledJobQueue(factory),
            services.GetRequiredService<IServiceScopeFactory>());
        var processed = await worker.RunOnceAsync(now);
        await using var verification = new ApprovalDbContext(options);
        var outbox = await verification.OutboxItems.SingleAsync(item => item.Id == "outbox-worker");
        var job = await verification.ScheduledJobs.SingleAsync(item => item.Id == "scheduled-worker");
        check("后台执行器分别调用待发和定时处理器", processed == 2 && outboxHandler.Calls == 1 &&
            jobHandler.Calls == 1);
        check("后台执行成功后两类项目均持久标记完成", outbox.Status == JobStatuses.Completed &&
            job.Status == JobStatuses.Completed);
        worker.Dispose();
    }

    /// <summary>
    /// 测试用待发发送器。
    /// </summary>
    private sealed class RecordingOutboxHandler : IOutboxHandler
    {
        /// <inheritdoc />
        public string Kind => "callback";

        /// <summary>
        /// 已调用次数。
        /// </summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 测试用定时任务处理器。
    /// </summary>
    private sealed class RecordingScheduledJobHandler : IScheduledJobHandler
    {
        /// <inheritdoc />
        public string Kind => "reminder";

        /// <summary>
        /// 已调用次数。
        /// </summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public Task ExecuteAsync(ScheduledJobEntity job, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
