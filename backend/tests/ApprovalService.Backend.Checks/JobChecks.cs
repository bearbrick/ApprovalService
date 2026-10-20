using System.Data.Common;
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
            await CheckExpiredWorkerLeaseAsync(options, check);
            await CheckDatabaseOutageAsync(directory, check);
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
        var clock = new AdvancingTimeProvider();
        var worker = new DurableJobWorker(new OutboxQueue(factory), new ScheduledJobQueue(factory),
            services.GetRequiredService<IServiceScopeFactory>(), clock);
        var processed = await worker.RunOnceAsync(now);
        await using var verification = new ApprovalDbContext(options);
        var outbox = await verification.OutboxItems.SingleAsync(item => item.Id == "outbox-worker");
        var job = await verification.ScheduledJobs.SingleAsync(item => item.Id == "scheduled-worker");
        check("后台执行器分别调用待发和定时处理器", processed == 2 && outboxHandler.Calls == 1 &&
            jobHandler.Calls == 1);
        check("后台执行成功后两类项目均持久标记完成", outbox.Status == JobStatuses.Completed &&
            job.Status == JobStatuses.Completed);

        await using (var context = new ApprovalDbContext(options))
        {
            context.OutboxItems.Add(new OutboxItemEntity
            {
                Id = "outbox-failure",
                InstanceId = "instance",
                EventId = "event-failure",
                Kind = "callback",
                PayloadJson = "{}",
                AvailableAt = now.AddMinutes(1),
            });
            await context.SaveChangesAsync();
        }

        outboxHandler.FailNext = true;
        var firstAttempt = await worker.RunOnceAsync(now.AddMinutes(1));
        await using var failedVerification = new ApprovalDbContext(options);
        var failed = await failedVerification.OutboxItems.AsNoTracking()
            .SingleAsync(item => item.Id == "outbox-failure");
        check("后台发送器异常会持久安排重试", firstAttempt == 1 && failed.Status == JobStatuses.Pending &&
            failed.Attempts == 1 && failed.LastError == nameof(InvalidOperationException) &&
            failed.AvailableAt == now.AddMinutes(2));
        var retryAttempt = await worker.RunOnceAsync(now.AddMinutes(2));
        await using var retryVerification = new ApprovalDbContext(options);
        var completed = await retryVerification.OutboxItems.AsNoTracking()
            .SingleAsync(item => item.Id == "outbox-failure");
        check("后台发送器重试成功后只确认一次", retryAttempt == 1 &&
            completed.Status == JobStatuses.Completed && completed.Attempts == 2);
        worker.Dispose();
    }

    /// <summary>
    /// 处理器跨过租约截止时间时，旧执行者不能用领取时的时间确认完成。
    /// </summary>
    /// <param name="options">同一数据库的连接选项。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckExpiredWorkerLeaseAsync(DbContextOptions<ApprovalDbContext> options,
        Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.Zero);
        await using (var context = new ApprovalDbContext(options))
        {
            context.OutboxItems.Add(new OutboxItemEntity
            {
                Id = "outbox-expired",
                InstanceId = "instance",
                EventId = "event-expired",
                Kind = "slow-callback",
                PayloadJson = "{}",
                AvailableAt = now,
            });
            context.ScheduledJobs.Add(new ScheduledJobEntity
            {
                Id = "scheduled-expired",
                Kind = "slow-reminder",
                PayloadJson = "{}",
                DueAt = now,
            });
            await context.SaveChangesAsync();
        }

        var clock = new AdvancingTimeProvider();
        using var services = new ServiceCollection()
            .AddSingleton<IOutboxHandler>(new AdvancingOutboxHandler(clock))
            .AddSingleton<IScheduledJobHandler>(new AdvancingScheduledJobHandler(clock))
            .BuildServiceProvider();
        var factory = new PooledDbContextFactory<ApprovalDbContext>(options);
        using var worker = new DurableJobWorker(new OutboxQueue(factory), new ScheduledJobQueue(factory),
            services.GetRequiredService<IServiceScopeFactory>(), clock);
        await worker.RunOnceAsync(now);
        await using (var context = new ApprovalDbContext(options))
        {
            var outbox = await context.OutboxItems.AsNoTracking().SingleAsync(item => item.Id == "outbox-expired");
            var job = await context.ScheduledJobs.AsNoTracking().SingleAsync(item => item.Id == "scheduled-expired");
            check("处理器运行超过租约后旧执行者不能确认待发项或定时任务",
                outbox.Status == JobStatuses.Leased && job.Status == JobStatuses.Leased);
        }

        var reclaimedOutbox = await new OutboxQueue(factory).ClaimNextAsync(now.AddMinutes(6),
            TimeSpan.FromMinutes(2));
        var reclaimedJob = await new ScheduledJobQueue(factory).ClaimNextAsync(now.AddMinutes(6),
            TimeSpan.FromMinutes(2));
        check("租约过期的两类工作仍能由新执行者领取", reclaimedOutbox?.Id == "outbox-expired" &&
            reclaimedJob?.Id == "scheduled-expired");
    }

    /// <summary>
    /// 数据库临时不可达时后台执行器保持运行，等待连接恢复。
    /// </summary>
    /// <param name="directory">测试目录。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckDatabaseOutageAsync(string directory, Action<string, bool> check)
    {
        var missingPath = Path.Combine(directory, "absent", "jobs.db");
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseSqlite("Data Source=" + missingPath).Options;
        var factory = new PooledDbContextFactory<ApprovalDbContext>(options);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var worker = new DurableJobWorker(new OutboxQueue(factory), new ScheduledJobQueue(factory),
            services.GetRequiredService<IServiceScopeFactory>());
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        check("数据库连接失败时后台执行器保持运行等待重试", worker.ExecuteTask is { IsCompleted: false });
        await worker.StopAsync(CancellationToken.None);

        using var wrappedWorker = new DurableJobWorker(new OutboxQueue(new TransientFailureFactory()),
            new ScheduledJobQueue(new TransientFailureFactory()),
            services.GetRequiredService<IServiceScopeFactory>());
        await wrappedWorker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        check("提供程序把连接错误包装为瞬断异常时后台执行器仍等待重试",
            wrappedWorker.ExecuteTask is { IsCompleted: false });
        await wrappedWorker.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// 模拟数据库提供程序把连接异常包进瞬断异常的工厂。
    /// </summary>
    private sealed class TransientFailureFactory : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => throw new InvalidOperationException(
            "数据库连接暂时不可用。", new TestDatabaseException());

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ApprovalDbContext>(new InvalidOperationException(
                "数据库连接暂时不可用。", new TestDatabaseException()));
    }

    /// <summary>
    /// 测试用数据库连接异常。
    /// </summary>
    private sealed class TestDatabaseException : DbException
    {
    }

    /// <summary>
    /// 以手动推进的单调时钟模拟处理器跨过租约期限。
    /// </summary>
    private sealed class AdvancingTimeProvider : TimeProvider
    {
        /// <summary>
        /// 当前单调时钟刻度。
        /// </summary>
        private long ticks;

        /// <inheritdoc />
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <inheritdoc />
        public override long GetTimestamp() => Interlocked.Read(ref ticks);

        /// <summary>
        /// 推进测试时钟。
        /// </summary>
        /// <param name="duration">前进的时长。</param>
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }

    /// <summary>
    /// 模拟耗时超过租约的待发处理器。
    /// </summary>
    /// <param name="clock">可手动推进的时钟。</param>
    private sealed class AdvancingOutboxHandler(AdvancingTimeProvider clock) : IOutboxHandler
    {
        /// <inheritdoc />
        public string Kind => "slow-callback";

        /// <inheritdoc />
        public Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromMinutes(3));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 模拟耗时超过租约的定时任务处理器。
    /// </summary>
    /// <param name="clock">可手动推进的时钟。</param>
    private sealed class AdvancingScheduledJobHandler(AdvancingTimeProvider clock) : IScheduledJobHandler
    {
        /// <inheritdoc />
        public string Kind => "slow-reminder";

        /// <inheritdoc />
        public Task ExecuteAsync(ScheduledJobEntity job, CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromMinutes(3));
            return Task.CompletedTask;
        }
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
        public int Calls
        {
            get; private set;
        }

        /// <summary>
        /// 下一次调用是否故意失败。
        /// </summary>
        public bool FailNext
        {
            get; set;
        }

        /// <inheritdoc />
        public Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken)
        {
            Calls++;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("模拟发送失败。");
            }

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
        public int Calls
        {
            get; private set;
        }

        /// <inheritdoc />
        public Task ExecuteAsync(ScheduledJobEntity job, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
