using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// SQLite 启动时执行迁移，使重启后的队列和定时任务从原数据库继续运行。
/// </summary>
/// <param name="factory">SQLite 上下文工厂。</param>
/// <param name="configuration">数据目录配置。</param>
internal sealed class SqliteDatabaseInitializer(IDbContextFactory<SqliteApprovalDbContext> factory, IConfiguration configuration) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = configuration["ApprovalService:Data:Directory"] ?? "data";
        var absolute = Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory);
        Directory.CreateDirectory(absolute);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
