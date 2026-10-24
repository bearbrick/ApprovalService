using System.Net;
using System.Net.Http.Headers;
using ApprovalService.Api.Hosting;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Infrastructure.Hosting;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 本机指标的维度限制与 SQLite 快照、恢复场景。
/// </summary>
internal static class DeploymentChecks
{
    /// <summary>
    /// 运行指标和活动数据库快照检查。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">记录检查结果。</param>
    /// <returns>场景结束。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        await CheckMetricsAsync(backendRoot, check);
        await CheckBackupRestoreAsync(check);
        await CheckPreupgradeSnapshotAsync(backendRoot, check);
    }

    /// <summary>
    /// 检查真实 HTTP 请求统计及默认只允许本机采集的边界。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">记录检查结果。</param>
    /// <returns>HTTP 场景结束。</returns>
    private static async Task CheckMetricsAsync(string backendRoot, Action<string, bool> check)
    {
        var data = TempDataDirectory.Create();
        try
        {
            await using var factory = new ServerFactory(backendRoot, data);
            using var client = factory.CreateClient();
            var info = await client.GetAsync(ApiRoutes.Info);
            var actual = factory.Services.GetRequiredService<RequestMetrics>().Render();
            check("HTTP 指标只记录服务端模板、方法与状态，不泄漏实际请求值",
                info.StatusCode == HttpStatusCode.OK &&
                actual.Contains("route=\"/api/v1/info\",status=\"200\"", StringComparison.Ordinal) &&
                actual.Contains("approval_http_request_duration_seconds_bucket", StringComparison.Ordinal) &&
                !actual.Contains("traceId", StringComparison.Ordinal));

            var metrics = new RequestMetrics();
            metrics.Observe("GET", "/api/v1/instances/{id}", 200, 0.007);
            metrics.Observe("GET", "/api/v1/instances/{id}", 200, 0.04);
            var rendered = metrics.Render();
            check("请求直方图按固定秒数边界累计且不输出真实审批单 ID",
                rendered.Contains("le=\"0.005\"} 0", StringComparison.Ordinal) &&
                rendered.Contains("le=\"0.05\"} 2", StringComparison.Ordinal) &&
                rendered.Contains("approval_http_requests_total{method=\"GET\",route=\"/api/v1/instances/{id}\",status=\"200\"} 2", StringComparison.Ordinal));

            var denied = await client.GetAsync(ApiRoutes.Metrics);
            check("无本机连接地址的测试客户端不能读取运行指标", denied.StatusCode == HttpStatusCode.Forbidden);

            const string token = "test-only-metrics-token-32-characters";
            var protectedData = TempDataDirectory.Create();
            try
            {
                await using var protectedFactory = new ServerFactory(backendRoot, protectedData,
                    new Dictionary<string, string?> { ["ApprovalService:Metrics:BearerToken"] = token });
                using var scraper = protectedFactory.CreateClient();
                scraper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var accepted = await scraper.GetAsync(ApiRoutes.Metrics);
                check("配置了长采集密钥后远程采集只返回 Prometheus 文本",
                    accepted.StatusCode == HttpStatusCode.OK &&
                    accepted.Content.Headers.ContentType?.MediaType == "text/plain" &&
                    (await accepted.Content.ReadAsStringAsync()).Contains("approval_http_requests_total", StringComparison.Ordinal));
            }
            finally
            {
                TempDataDirectory.Delete(protectedData);
            }
        }
        finally
        {
            TempDataDirectory.Delete(data);
        }
    }

    /// <summary>
    /// 在 WAL 活动连接下生成快照，再从快照恢复到另一数据库并核对附件与密钥。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    /// <returns>恢复演练结束。</returns>
    private static async Task CheckBackupRestoreAsync(Action<string, bool> check)
    {
        var data = TempDataDirectory.Create();
        try
        {
            var source = Path.Combine(data, SqliteBackupService.DatabaseFileName);
            await using var live = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source }.ToString());
            await live.OpenAsync();
            await ExecuteAsync(live, "PRAGMA journal_mode=WAL;");
            await ExecuteAsync(live, "CREATE TABLE BackupProbe(Value TEXT NOT NULL);");
            await ExecuteAsync(live, "INSERT INTO BackupProbe(Value) VALUES ('before');");
            var file = Path.Combine(data, "files", "2026", "10", new string('a', 32));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "attachment-before");
            await File.WriteAllTextAsync(Path.Combine(data, SqliteBackupService.IdentityKeyFileName), "test-only-key");

            var backup = new SqliteBackupService(Options.Create(new DataOptions { Directory = data }),
                Options.Create(new BackupOptions { Keep = 2 }), TimeProvider.System,
                NullLogger<SqliteBackupService>.Instance);
            var first = await backup.CreateAsync("daily");
            check("活动 WAL 数据库备份带完整主库、本地附件和登录签名密钥",
                File.Exists(Path.Combine(first, SqliteBackupService.DatabaseFileName)) &&
                await File.ReadAllTextAsync(Path.Combine(first, "files", "2026", "10", new string('a', 32))) == "attachment-before" &&
                await File.ReadAllTextAsync(Path.Combine(first, SqliteBackupService.IdentityKeyFileName)) == "test-only-key" &&
                backup.HasDailyBackup(DateOnly.FromDateTime(DateTime.Now)));

            await ExecuteAsync(live, "UPDATE BackupProbe SET Value = 'after';");
            var restored = Path.Combine(data, "restored.db");
            File.Copy(Path.Combine(first, SqliteBackupService.DatabaseFileName), restored);
            await using var restoredConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = restored }.ToString());
            await restoredConnection.OpenAsync();
            await using var query = restoredConnection.CreateCommand();
            query.CommandText = "SELECT Value FROM BackupProbe LIMIT 1";
            check("从快照恢复能读取备份时的值而非后续写入", (string?)await query.ExecuteScalarAsync() == "before");

            var upgrade = await backup.CreateAsync("preupgrade");
            await backup.CreateAsync("daily");
            await backup.CreateAsync("daily");
            check("每日快照只保留最近两份，升级前快照不会被自动删除",
                Directory.EnumerateDirectories(backup.BackupDirectory, "daily-*").Count() == 2 &&
                File.Exists(Path.Combine(upgrade, "complete.marker")));
        }
        finally
        {
            TempDataDirectory.Delete(data);
        }
    }

    /// <summary>
    /// 用仅应用首个迁移的真实旧库检查启动时先留快照再升级。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">记录检查结果。</param>
    /// <returns>升级检查完成。</returns>
    private static async Task CheckPreupgradeSnapshotAsync(string backendRoot, Action<string, bool> check)
    {
        var data = TempDataDirectory.Create();
        try
        {
            var file = Path.Combine(data, SqliteBackupService.DatabaseFileName);
            var options = new DbContextOptionsBuilder<SqliteApprovalDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = file }.ToString()).Options;
            await using (var old = new SqliteApprovalDbContext(options))
            {
                var first = old.Database.GetMigrations().First();
                await old.GetService<IMigrator>().MigrateAsync(first);
            }

            await using var factory = new ServerFactory(backendRoot, data);
            using var client = factory.CreateClient();
            var ready = await client.GetAsync(ApiRoutes.HealthReady);
            var snapshots = Directory.EnumerateDirectories(Path.Combine(data, "backups"), "preupgrade-*").ToArray();
            check("现存 SQLite 库有待执行迁移时先留完整升级前快照再对外就绪",
                ready.StatusCode == HttpStatusCode.OK && snapshots.Length == 1 &&
                File.Exists(Path.Combine(snapshots[0], "complete.marker")) &&
                File.Exists(Path.Combine(snapshots[0], SqliteBackupService.DatabaseFileName)));
        }
        finally
        {
            TempDataDirectory.Delete(data);
        }
    }

    /// <summary>
    /// 对临时 SQLite 连接执行一条测试 SQL。
    /// </summary>
    /// <param name="connection">已打开的连接。</param>
    /// <param name="sql">测试 SQL。</param>
    /// <returns>命令完成。</returns>
    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
