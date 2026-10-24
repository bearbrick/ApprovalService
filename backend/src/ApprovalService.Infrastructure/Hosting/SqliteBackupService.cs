using System.Globalization;
using ApprovalService.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Hosting;

/// <summary>
/// 给 SQLite 主库生成在线一致快照，再复制不可变的本地附件与登录签名密钥。
/// </summary>
/// <param name="data">数据库目录配置。</param>
/// <param name="options">每日保留设置。</param>
/// <param name="clock">时间来源。</param>
/// <param name="logger">不记录备份内容的日志。</param>
public sealed class SqliteBackupService(IOptions<DataOptions> data, IOptions<BackupOptions> options,
    TimeProvider clock, ILogger<SqliteBackupService> logger)
{
    /// <summary>
    /// 禁止每日任务和迁移前快照同时操作同一目录。
    /// </summary>
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// 数据目录中的 SQLite 文件名。
    /// </summary>
    public const string DatabaseFileName = "approval.db";

    /// <summary>
    /// 登录签名密钥文件名。
    /// </summary>
    public const string IdentityKeyFileName = "identity-signing.key";

    /// <summary>
    /// 当前 SQLite 数据目录的绝对路径。
    /// </summary>
    public string DataDirectory => Path.GetFullPath(Path.IsPathRooted(data.Value.Directory)
        ? data.Value.Directory : Path.Combine(AppContext.BaseDirectory, data.Value.Directory));

    /// <summary>
    /// 备份根目录，不允许放到 SQLite 主库目录以外的任意请求路径。
    /// </summary>
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    /// <summary>
    /// 当天每日快照是否已经完成，用于服务重启后防止重复生成。
    /// </summary>
    /// <param name="localDate">服务器本地日期。</param>
    /// <returns>是否存在带完成标记的当天快照。</returns>
    public bool HasDailyBackup(DateOnly localDate)
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return false;
        }

        var prefix = "daily-" + localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-";
        return Directory.EnumerateDirectories(BackupDirectory, prefix + "*")
            .Any(directory => File.Exists(Path.Combine(directory, "complete.marker")));
    }

    /// <summary>
    /// 生成一份每日或升级前快照；失败时删除半成品，不影响现有备份。
    /// </summary>
    /// <param name="kind">只能是 daily 或 preupgrade。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完整快照目录。</returns>
    public async Task<string> CreateAsync(string kind, CancellationToken cancellationToken = default)
    {
        if (data.Value.Provider != "Sqlite")
        {
            throw new InvalidOperationException("内置备份只支持 SQLite。");
        }

        if (kind is not ("daily" or "preupgrade"))
        {
            throw new ArgumentException("备份类型无效。", nameof(kind));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var source = Path.Combine(DataDirectory, DatabaseFileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("SQLite 主库文件不存在，无法备份。", source);
            }

            Directory.CreateDirectory(BackupDirectory);
            var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Local);
            var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = Path.Combine(BackupDirectory, kind + "-" + stamp + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(target);
            try
            {
                // VACUUM INTO 得到完整的某一时刻快照，不直接复制带 WAL 的活动库文件。
                await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = source,
                    Mode = SqliteOpenMode.ReadWrite,
                }.ToString()))
                {
                    await connection.OpenAsync(cancellationToken);
                    await using var command = connection.CreateCommand();
                    command.CommandText = "VACUUM INTO $target";
                    command.Parameters.AddWithValue("$target", Path.Combine(target, DatabaseFileName));
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await CopyFilesAsync(Path.Combine(DataDirectory, "files"), Path.Combine(target, "files"), cancellationToken);
                var identityKey = Path.Combine(DataDirectory, IdentityKeyFileName);
                if (File.Exists(identityKey))
                {
                    File.Copy(identityKey, Path.Combine(target, IdentityKeyFileName));
                }

                // 标记最后落盘；每日任务只认完整目录。
                await File.WriteAllTextAsync(Path.Combine(target, "complete.marker"),
                    now.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
                if (kind == "daily")
                {
                    PruneDaily();
                }

                logger.LogInformation("SQLite {Kind} 快照已完成：{Target}", kind, target);
                return target;
            }
            catch
            {
                Directory.Delete(target, recursive: true);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 复制按年月组织的本地附件，不跟随指向数据目录外的重解析点。
    /// </summary>
    /// <param name="source">附件源目录。</param>
    /// <param name="target">本次快照中的附件目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>复制结束。</returns>
    private static async Task CopyFilesAsync(string source, string target, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var file in Directory.EnumerateFiles(source, "*", enumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                80 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 80 * 1024, FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    /// <summary>
    /// 只清理已完成的旧每日快照，不自动删除升级前快照。
    /// </summary>
    private void PruneDaily()
    {
        var old = new DirectoryInfo(BackupDirectory).EnumerateDirectories("daily-*")
            .Where(directory => File.Exists(Path.Combine(directory.FullName, "complete.marker")))
            .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
            .Skip(options.Value.Keep);
        foreach (var directory in old)
        {
            directory.Delete(recursive: true);
        }
    }
}
