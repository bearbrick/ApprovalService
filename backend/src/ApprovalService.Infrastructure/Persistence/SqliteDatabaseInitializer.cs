using ApprovalService.Infrastructure.Hosting;
using ApprovalService.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// SQLite 启动时执行迁移，使重启后的队列和定时任务从原数据库继续运行。
/// </summary>
/// <param name="factory">SQLite 上下文工厂。</param>
/// <param name="options">最终数据目录与提供程序配置。</param>
/// <param name="backups">升级前的在线快照服务。</param>
internal sealed class SqliteDatabaseInitializer(IDbContextFactory<SqliteApprovalDbContext> factory,
    IOptions<DataOptions> options, SqliteBackupService backups) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Provider != "Sqlite")
        {
            return;
        }

        var directory = options.Value.Directory;
        var absolute = Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory);
        Directory.CreateDirectory(absolute);
        var existed = File.Exists(Path.Combine(absolute, SqliteBackupService.DatabaseFileName));
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (existed && (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            // 升级事务开始前保留旧库和本地附件；快照失败时拒绝继续迁移。
            await backups.CreateAsync("preupgrade", cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();
        return Task.CompletedTask;
    }
}
