using System.Globalization;
using ApprovalService.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Hosting;

/// <summary>
/// 按服务器本地日期每天检查一次 SQLite 快照，重启后也不会重复备份。
/// </summary>
/// <param name="options">每日备份时间和启用状态。</param>
/// <param name="data">数据库提供程序。</param>
/// <param name="backup">快照服务。</param>
/// <param name="clock">可替换的时间来源。</param>
/// <param name="logger">只记录失败类别的日志。</param>
internal sealed class DailyBackupWorker(IOptions<BackupOptions> options, IOptions<DataOptions> data,
    SqliteBackupService backup, TimeProvider clock, ILogger<DailyBackupWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || data.Value.Provider != "Sqlite")
        {
            return;
        }

        var backupTime = TimeOnly.ParseExact(options.Value.Time, "HH:mm", CultureInfo.InvariantCulture);
        while (!stoppingToken.IsCancellationRequested)
        {
            var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Local);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (TimeOnly.FromDateTime(local.DateTime) >= backupTime && !backup.HasDailyBackup(date))
            {
                try
                {
                    await backup.CreateAsync("daily", stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error) when (error is IOException or SqliteException or UnauthorizedAccessException)
                {
                    logger.LogError(error, "SQLite 每日备份失败，稍后重试");
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
        }
    }
}
