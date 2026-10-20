using ApprovalService.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// SQLite 启动时执行迁移，使重启后的队列和定时任务从原数据库继续运行。
/// </summary>
/// <param name="factory">SQLite 上下文工厂。</param>
/// <param name="options">最终数据目录与提供程序配置。</param>
internal sealed class SqliteDatabaseInitializer(IDbContextFactory<SqliteApprovalDbContext> factory, IOptions<DataOptions> options) : IHostedService
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
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
