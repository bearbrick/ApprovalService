using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 自动动作十次失败后由管理员用例重排原事件的持久场景。
/// </summary>
internal static partial class JobChecks
{
    /// <summary>
    /// 验证自动上限、待管理员状态与同事件身份的单次人工重试。
    /// </summary>
    /// <param name="options">已创建的 SQLite 数据库配置。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckActionRetryAsync(DbContextOptions<ApprovalDbContext> options,
        Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        const string eventId = "instance-action/request/event/1";
        await using (var db = new ApprovalDbContext(options))
        {
            db.OutboxItems.Add(new OutboxItemEntity
            {
                Id = eventId + ":dispatch",
                InstanceId = "instance-action",
                EventId = eventId,
                Kind = "engineEvent",
                PayloadJson = JsonSerializer.Serialize(new EngineEvent
                {
                    Id = eventId,
                    Kind = "actionPending",
                    NodeId = "automatic",
                    TokenId = "token-action",
                }, ApprovalJson.Options),
                Attempts = 8,
                AvailableAt = now,
            });
            await db.SaveChangesAsync();
        }

        var factory = new PooledDbContextFactory<ApprovalDbContext>(options);
        var queue = new OutboxQueue(factory);
        var ninth = await queue.ClaimNextAsync(now, TimeSpan.FromMinutes(2));
        check("自动动作第九次失败仍按退避表重新排队", ninth?.Attempts == 9 &&
            await queue.FailAsync(ninth, now, "BusinessHookFailed"));
        var tenthAt = now.AddDays(2);
        var tenth = await queue.ClaimNextAsync(tenthAt, TimeSpan.FromMinutes(2));
        check("自动动作第十次领取仍保留原事件身份", tenth?.Attempts == 10 && tenth.EventId == eventId);
        check("第十次失败转待管理员处理且不再自动领取", await queue.FailAsync(tenth!, tenthAt,
            "BusinessHookFailed") && await queue.ClaimNextAsync(tenthAt.AddYears(1), TimeSpan.FromMinutes(2)) is null);

        await using (var db = new ApprovalDbContext(options))
        {
            var paused = await db.OutboxItems.AsNoTracking().SingleAsync(item => item.EventId == eventId);
            check("管理员待处理状态持久保存原正文和十次尝试次数",
                paused.Status == JobStatuses.NeedsAttention && paused.Attempts == 10 &&
                paused.EventId == eventId && paused.PayloadJson.Contains("actionPending", StringComparison.Ordinal));
        }

        var repository = new ActionRetryRepository(factory);
        var clock = new RetryClock(tenthAt.AddMinutes(1));
        var service = new ActionRetryAppService(repository, clock);
        await service.RetryAsync(eventId);
        await using (var db = new ApprovalDbContext(options))
        {
            var requeued = await db.OutboxItems.AsNoTracking().SingleAsync(item => item.EventId == eventId);
            check("人工重试只重排原事件并保留同一正文", requeued.Status == JobStatuses.Pending &&
                requeued.Attempts == 9 && requeued.Id == eventId + ":dispatch");
        }

        var manual = await queue.ClaimNextAsync(clock.GetUtcNow(), TimeSpan.FromMinutes(2));
        check("管理员的一次人工重试仍使用原 eventId", manual?.Attempts == 10 && manual.EventId == eventId);
        check("人工重试再次失败立即回到待管理员处理", await queue.FailAsync(manual!, clock.GetUtcNow(),
            "BusinessHookFailed") && await queue.ClaimNextAsync(clock.GetUtcNow().AddDays(3),
                TimeSpan.FromMinutes(2)) is null);

        // 回调与其他待发事件不受自动动作十次限制。
        await using (var db = new ApprovalDbContext(options))
        {
            db.OutboxItems.Add(new OutboxItemEntity
            {
                Id = "callback-tenth",
                InstanceId = "instance-callback",
                EventId = "callback-event",
                Kind = "callback",
                PayloadJson = "{}",
                Attempts = 9,
                AvailableAt = now,
            });
            await db.SaveChangesAsync();
        }

        var callback = await queue.ClaimNextAsync(clock.GetUtcNow(), TimeSpan.FromMinutes(2));
        check("其他待发项第十次失败继续使用原重试规则", callback?.Kind == "callback" &&
            await queue.FailAsync(callback, clock.GetUtcNow(), "CallbackFailure"));
        await using (var db = new ApprovalDbContext(options))
        {
            var ordinary = await db.OutboxItems.AsNoTracking().SingleAsync(item => item.Id == "callback-tenth");
            check("回调不会误入自动动作的待管理员状态", ordinary.Status == JobStatuses.Pending);
        }
    }

    /// <summary>
    /// 供管理员重排场景注入固定 UTC 时间。
    /// </summary>
    /// <param name="now">固定时间。</param>
    private sealed class RetryClock(DateTimeOffset now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;
    }
}
