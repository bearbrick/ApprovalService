using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 在进程内启动真实服务：开发环境配置、指定的数据目录，只输出警告以上的日志。
/// </summary>
/// <remarks>
/// 内容根目录要显式指定：WebApplicationFactory 默认按 .sln 推断，不认识 .slnx。
/// </remarks>
/// <param name="backendRoot">backend 目录。</param>
/// <param name="dataDirectory">数据目录（一般是 <see cref="TempDataDirectory.Create"/> 新建的临时目录）。</param>
/// <param name="settings">额外的配置项，覆盖默认值；null 表示没有。</param>
internal sealed class ServerFactory(string backendRoot, string dataDirectory, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<global::Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var values = new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning",
            ["ApprovalService:Data:Directory"] = dataDirectory,
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        builder.UseEnvironment("Development")
            .UseContentRoot(Path.Combine(backendRoot, "src", "ApprovalService.Api"))
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(values));
    }
}

/// <summary>
/// 检查程序用的临时数据目录：每次新建，跑完删除。
/// </summary>
internal static class TempDataDirectory
{
    /// <summary>
    /// 在系统临时目录下新建一个空目录。
    /// </summary>
    /// <returns>新目录的完整路径。</returns>
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "approval-service-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 清空 SQLite 连接池后删除临时数据目录，避免 Windows 锁住数据库文件。
    /// </summary>
    /// <param name="path">临时数据目录；不存在时什么也不做。</param>
    public static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(path, recursive: true);
        }
    }
}
