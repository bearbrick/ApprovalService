using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// 按管理员配置的本地时间为当天建立一次可重启的汇总任务。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
/// <param name="settings">通知设置仓储。</param>
/// <param name="configuration">日历时区配置。</param>
public sealed class DailyDigestScheduler(IDbContextFactory<ApprovalDbContext> factory,
    INotificationSettingsRepository settings, IConfiguration configuration)
{
    /// <summary>
    /// 每日发送时间使用与节点期限相同的组织时区。
    /// </summary>
    private readonly TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(
        configuration["ApprovalService:Notifications:Calendar:TimeZoneId"] ?? "Asia/Shanghai");

    /// <summary>
    /// 若今日已到管理员配置的时间，则幂等排入一项持久汇总任务。
    /// </summary>
    /// <param name="now">本次固定 UTC 时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>新建了任务为真。</returns>
    public async Task<bool> ScheduleIfDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var sendTime = await settings.GetDailyDigestTimeAsync(cancellationToken);
        if (sendTime is null)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(now, timeZone);
        var planned = TimeOnly.ParseExact(sendTime, "HH:mm", CultureInfo.InvariantCulture);
        if (TimeOnly.FromDateTime(local.DateTime) < planned)
        {
            return false;
        }

        var date = DateOnly.FromDateTime(local.DateTime);
        var id = "daily-digest-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
            sendTime.Replace(":", string.Empty, StringComparison.Ordinal);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.ScheduledJobs.AsNoTracking().AnyAsync(job => job.Id == id, cancellationToken))
        {
            return false;
        }

        db.ScheduledJobs.Add(new ScheduledJobEntity
        {
            Id = id,
            Kind = DailyDigestJobHandler.JobKind,
            Status = JobStatuses.Pending,
            DueAt = now,
            PayloadJson = JsonSerializer.Serialize(new DailyDigestJobPayload
            {
                LocalDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                LocalSendTime = sendTime,
            }),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // 多实例同一秒建任务时主键去重；其他写库错误继续暴露。
            await using var retry = await factory.CreateDbContextAsync(cancellationToken);
            if (await retry.ScheduledJobs.AsNoTracking().AnyAsync(job => job.Id == id, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }
}

/// <summary>
/// 周期检查管理员汇总时刻；实际发送交给已有持久定时任务执行器。
/// </summary>
/// <param name="scopeFactory">每轮创建独立依赖范围。</param>
/// <param name="timeProvider">可替换时钟。</param>
/// <param name="logger">数据库暂时不可用时记录诊断。</param>
public sealed class DailyDigestHostedService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<DailyDigestHostedService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DailyDigestScheduler>()
                    .ScheduleIfDueAsync(timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (Exception error) when (error is DbException or
                DbUpdateException { InnerException: DbException } or
                InvalidOperationException { InnerException: DbException })
            {
                // 启动迁移或暂时断库期间下一轮再检查，持久任务不会丢失。
                logger.LogError(error, "每日待办汇总排程暂时无法访问数据库。");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), timeProvider, stoppingToken);
        }
    }
}

/// <summary>
/// 持久任务记录的本地日期和原管理员设置，用于拒绝已过期的排程。
/// </summary>
internal sealed class DailyDigestJobPayload
{
    /// <summary>
    /// 管理员时区中的日历日期。
    /// </summary>
    public string LocalDate { get; set; } = string.Empty;

    /// <summary>
    /// 建立任务时的 HH:mm。
    /// </summary>
    public string LocalSendTime { get; set; } = string.Empty;
}
