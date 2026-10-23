using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// SQLite 专属迁移上下文，实体模型来自统一基类。
/// </summary>
/// <param name="options">SQLite 配置。</param>
public sealed class SqliteApprovalDbContext(DbContextOptions<SqliteApprovalDbContext> options) : ApprovalDbContext(options);

/// <summary>
/// SQL Server 专属迁移上下文，实体模型来自统一基类。
/// </summary>
/// <param name="options">SQL Server 配置。</param>
public sealed class SqlServerApprovalDbContext(DbContextOptions<SqlServerApprovalDbContext> options) : ApprovalDbContext(options);

/// <summary>
/// PostgreSQL 专属迁移上下文，实体模型来自统一基类。
/// </summary>
/// <param name="options">PostgreSQL 配置。</param>
public sealed class PostgreSqlApprovalDbContext(DbContextOptions<PostgreSqlApprovalDbContext> options) : ApprovalDbContext(options);

/// <summary>
/// 仅供 EF CLI 生成 SQLite 迁移，不连接实际库。
/// </summary>
public sealed class SqliteApprovalDesignFactory : IDesignTimeDbContextFactory<SqliteApprovalDbContext>
{
    /// <inheritdoc />
    public SqliteApprovalDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqliteApprovalDbContext>().UseSqlite("Data Source=:memory:").Options);
}

/// <summary>
/// 仅供 EF CLI 生成 SQL Server 迁移，不连接实际库。
/// </summary>
public sealed class SqlServerApprovalDesignFactory : IDesignTimeDbContextFactory<SqlServerApprovalDbContext>
{
    /// <inheritdoc />
    public SqlServerApprovalDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqlServerApprovalDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ApprovalServiceDesign;Trusted_Connection=True;").Options);
}

/// <summary>
/// 仅供 EF CLI 生成 PostgreSQL 迁移，不连接实际库。
/// </summary>
public sealed class PostgreSqlApprovalDesignFactory : IDesignTimeDbContextFactory<PostgreSqlApprovalDbContext>
{
    /// <inheritdoc />
    public PostgreSqlApprovalDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PostgreSqlApprovalDbContext>().UseNpgsql("Host=localhost;Database=ApprovalServiceDesign;Username=design;Password=design").Options);
}
