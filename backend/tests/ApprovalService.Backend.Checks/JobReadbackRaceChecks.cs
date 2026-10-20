using System.Data.Common;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 在租约更新和回读之间暂停旧执行者，检查新租约不会被它误用。
/// </summary>
internal static class JobReadbackRaceChecks
{
    /// <summary>
    /// 在真实 SQLite 文件上分别重现待发项和定时任务的回读竞态。
    /// </summary>
    /// <param name="options">与 JobChecks 共用的 SQLite 上下文选项。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(DbContextOptions<ApprovalDbContext> options, Action<string, bool> check)
    {
        var factory = new PooledDbContextFactory<ApprovalDbContext>(options);
        var outboxNow = new DateTimeOffset(2026, 10, 20, 10, 30, 0, TimeSpan.Zero);
        await using (var context = new ApprovalDbContext(options))
        {
            context.OutboxItems.Add(new OutboxItemEntity
            {
                Id = "outbox-readback-race",
                InstanceId = "instance",
                EventId = "event-readback-race",
                Kind = "callback",
                PayloadJson = "{}",
                AvailableAt = outboxNow,
            });
            await context.SaveChangesAsync();
        }

        var outboxGate = new ReadbackGate("OutboxItems");
        var outboxOptions = new DbContextOptionsBuilder<ApprovalDbContext>(options)
            .AddInterceptors(outboxGate).Options;
        var outboxHandler = new CountingOutboxHandler();
        using (var services = new ServiceCollection().AddSingleton<IOutboxHandler>(outboxHandler)
            .BuildServiceProvider())
        using (var worker = new DurableJobWorker(
            new OutboxQueue(new PooledDbContextFactory<ApprovalDbContext>(outboxOptions)),
            new ScheduledJobQueue(factory), services.GetRequiredService<IServiceScopeFactory>()))
        {
            var oldWorker = worker.RunOnceAsync(outboxNow);
            OutboxItemEntity? newClaim;
            try
            {
                await outboxGate.WaitUntilBlockedAsync();
                newClaim = await new OutboxQueue(factory).ClaimNextAsync(outboxNow.AddMinutes(3),
                    TimeSpan.FromMinutes(2));
            }
            finally
            {
                outboxGate.Release();
            }

            var oldProcessed = await oldWorker;
            check("待发项回读前被重领时旧执行者不使用新租约发送",
                newClaim is not null && oldProcessed == 0 && outboxHandler.Calls == 0);
            check("待发项新租约仍可由当前执行者完成",
                await new OutboxQueue(factory).CompleteAsync(newClaim!, outboxNow.AddMinutes(3)));
        }

        var jobNow = outboxNow.AddMinutes(10);
        await using (var context = new ApprovalDbContext(options))
        {
            context.ScheduledJobs.Add(new ScheduledJobEntity
            {
                Id = "scheduled-readback-race",
                Kind = "reminder",
                PayloadJson = "{}",
                DueAt = jobNow,
            });
            await context.SaveChangesAsync();
        }

        var jobGate = new ReadbackGate("ScheduledJobs");
        var jobOptions = new DbContextOptionsBuilder<ApprovalDbContext>(options)
            .AddInterceptors(jobGate).Options;
        var jobHandler = new CountingScheduledJobHandler();
        using (var services = new ServiceCollection().AddSingleton<IScheduledJobHandler>(jobHandler)
            .BuildServiceProvider())
        using (var worker = new DurableJobWorker(new OutboxQueue(factory),
            new ScheduledJobQueue(new PooledDbContextFactory<ApprovalDbContext>(jobOptions)),
            services.GetRequiredService<IServiceScopeFactory>()))
        {
            var oldWorker = worker.RunOnceAsync(jobNow);
            ScheduledJobEntity? newClaim;
            try
            {
                await jobGate.WaitUntilBlockedAsync();
                newClaim = await new ScheduledJobQueue(factory).ClaimNextAsync(jobNow.AddMinutes(3),
                    TimeSpan.FromMinutes(2));
            }
            finally
            {
                jobGate.Release();
            }

            var oldProcessed = await oldWorker;
            check("定时任务回读前被重领时旧执行者不使用新租约执行",
                newClaim is not null && oldProcessed == 0 && jobHandler.Calls == 0);
            check("定时任务新租约仍可由当前执行者完成",
                await new ScheduledJobQueue(factory).CompleteAsync(newClaim!, jobNow.AddMinutes(3)));
        }
    }

    /// <summary>
    /// 只暂停指定表第二次 SELECT，也就是领取更新后的回读。
    /// </summary>
    /// <param name="tableName">要拦截的数据库表名。</param>
    private sealed class ReadbackGate(string tableName) : DbCommandInterceptor
    {
        /// <summary>
        /// 已发出的目标表 SELECT 数量。
        /// </summary>
        private int reads;

        /// <summary>
        /// 回读即将执行的通知。
        /// </summary>
        private readonly TaskCompletionSource<bool> blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 允许旧执行者继续的通知。
        /// </summary>
        private readonly TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(tableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Increment(ref reads) == 2)
            {
                blocked.TrySetResult(true);
                await released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        /// <summary>
        /// 等待旧执行者走到更新后的回读位置。
        /// </summary>
        /// <returns>异步操作。</returns>
        public Task WaitUntilBlockedAsync() => blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        /// <summary>
        /// 新执行者重领后让旧执行者完成回读。
        /// </summary>
        public void Release() => released.TrySetResult(true);
    }

    /// <summary>
    /// 记录旧执行者是否实际发送待发项。
    /// </summary>
    private sealed class CountingOutboxHandler : IOutboxHandler
    {
        /// <inheritdoc />
        public string Kind => "callback";

        /// <summary>
        /// 实际调用次数。
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
    /// 记录旧执行者是否实际执行定时任务。
    /// </summary>
    private sealed class CountingScheduledJobHandler : IScheduledJobHandler
    {
        /// <inheritdoc />
        public string Kind => "reminder";

        /// <summary>
        /// 实际调用次数。
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
