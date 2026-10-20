using ApprovalService.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 在最终配置生效后选择数据库提供程序，避免测试宿主和生产环境使用不同连接。
/// </summary>
/// <param name="services">三个数据库上下文工厂所在的服务容器。</param>
/// <param name="options">已通过启动校验的数据选项。</param>
internal sealed class SelectedApprovalDbContextFactory(IServiceProvider services, IOptions<DataOptions> options) : IDbContextFactory<ApprovalDbContext>
{
    /// <inheritdoc />
    public ApprovalDbContext CreateDbContext() => options.Value.Provider switch
    {
        "Sqlite" => services.GetRequiredService<IDbContextFactory<SqliteApprovalDbContext>>().CreateDbContext(),
        "SqlServer" => services.GetRequiredService<IDbContextFactory<SqlServerApprovalDbContext>>().CreateDbContext(),
        "PostgreSql" => services.GetRequiredService<IDbContextFactory<PostgreSqlApprovalDbContext>>().CreateDbContext(),
        _ => throw new InvalidOperationException("数据库提供程序配置不受支持。"),
    };

    /// <inheritdoc />
    public async Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => options.Value.Provider switch
    {
        "Sqlite" => await services.GetRequiredService<IDbContextFactory<SqliteApprovalDbContext>>().CreateDbContextAsync(cancellationToken),
        "SqlServer" => await services.GetRequiredService<IDbContextFactory<SqlServerApprovalDbContext>>().CreateDbContextAsync(cancellationToken),
        "PostgreSql" => await services.GetRequiredService<IDbContextFactory<PostgreSqlApprovalDbContext>>().CreateDbContextAsync(cancellationToken),
        _ => throw new InvalidOperationException("数据库提供程序配置不受支持。"),
    };
}
