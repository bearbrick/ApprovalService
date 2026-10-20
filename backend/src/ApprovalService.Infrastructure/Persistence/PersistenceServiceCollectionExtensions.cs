using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 按配置选择数据库提供程序，并向仓储和后台任务提供统一上下文工厂。
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// 注册数据库上下文、运行仓储和 SQLite 启动迁移。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">审批服务配置。</param>
    /// <returns>同一个服务集合。</returns>
    public static IServiceCollection AddApprovalPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var data = configuration.GetSection(DataOptions.SectionName).Get<DataOptions>() ?? new DataOptions();
        switch (data.Provider)
        {
            case "Sqlite":
                var directory = Path.IsPathRooted(data.Directory) ? data.Directory : Path.Combine(AppContext.BaseDirectory, data.Directory);
                var file = Path.Combine(directory, "approval.db");
                var connection = new SqliteConnectionStringBuilder { DataSource = file }.ToString();
                services.AddDbContextFactory<SqliteApprovalDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton<IDbContextFactory<ApprovalDbContext>>(provider =>
                    new DbContextFactoryAdapter<SqliteApprovalDbContext>(provider.GetRequiredService<IDbContextFactory<SqliteApprovalDbContext>>()));
                services.AddHostedService<SqliteDatabaseInitializer>();
                break;
            case "SqlServer":
                services.AddDbContextFactory<SqlServerApprovalDbContext>(options => options.UseSqlServer(data.ConnectionString));
                services.AddSingleton<IDbContextFactory<ApprovalDbContext>>(provider =>
                    new DbContextFactoryAdapter<SqlServerApprovalDbContext>(provider.GetRequiredService<IDbContextFactory<SqlServerApprovalDbContext>>()));
                break;
            case "PostgreSql":
                services.AddDbContextFactory<PostgreSqlApprovalDbContext>(options => options.UseNpgsql(data.ConnectionString));
                services.AddSingleton<IDbContextFactory<ApprovalDbContext>>(provider =>
                    new DbContextFactoryAdapter<PostgreSqlApprovalDbContext>(provider.GetRequiredService<IDbContextFactory<PostgreSqlApprovalDbContext>>()));
                break;
        }

        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContext());
        services.AddScoped<IApprovalRuntimeRepository, ApprovalRuntimeRepository>();
        return services;
    }
}
