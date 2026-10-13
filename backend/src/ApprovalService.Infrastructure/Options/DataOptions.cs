namespace ApprovalService.Infrastructure.Options;

/// <summary>
/// 数据配置（ApprovalService:Data）：用哪种数据库、数据目录在哪。MVP 用 SQLite，代码按三种数据库都能用的方式写（设计书第 9 节）。
/// </summary>
public sealed class DataOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "ApprovalService:Data";

    /// <summary>
    /// 数据库：Sqlite（默认）、SqlServer、PostgreSql。
    /// </summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>
    /// 数据目录：SQLite 库文件、本地附件、备份都放这里；相对路径按程序目录算。
    /// </summary>
    public string Directory { get; set; } = "data";

    /// <summary>
    /// SQL Server、PostgreSQL 的连接字符串；SQLite 不用填（按数据目录生成）。不要提交到仓库（AGENTS.md R7）。
    /// </summary>
    public string? ConnectionString
    {
        get; set;
    }
}
