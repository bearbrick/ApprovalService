using System.Security.Cryptography;
using ApprovalService.Application.Files;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Files;

/// <summary>
/// 逐个把本地附件复制到 MinIO，校验摘要后原子改写元数据，再删除本地副本。
/// </summary>
/// <param name="factory">附件元数据所在数据库工厂。</param>
/// <param name="selector">本地与对象存储选择器。</param>
/// <param name="fileOptions">当前文件存储配置。</param>
/// <param name="dataOptions">SQLite 数据目录配置。</param>
public sealed class LocalToMinioMigration(IDbContextFactory<ApprovalDbContext> factory,
    IFileStorageSelector selector, IOptions<FileStorageOptions> fileOptions,
    IOptions<DataOptions> dataOptions) : IFileMigrationService
{
    /// <summary>
    /// 迁移所有仍指向本地存储的附件，重启命令会从剩余项继续。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>成功迁移的附件数。</returns>
    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (fileOptions.Value.Provider != "Minio")
        {
            throw new InvalidOperationException("迁移前请把附件存储提供者配置为 Minio。");
        }

        if (dataOptions.Value.Provider == "Sqlite")
        {
            var directory = dataOptions.Value.Directory;
            Directory.CreateDirectory(Path.IsPathRooted(directory) ? directory :
                Path.Combine(AppContext.BaseDirectory, directory));
        }

        await using (var schema = await factory.CreateDbContextAsync(cancellationToken))
        {
            await schema.Database.MigrateAsync(cancellationToken);
        }

        var local = selector.Resolve("Local");
        var remote = selector.Resolve("Minio");
        var moved = 0;
        while (true)
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var file = await context.StoredFiles.AsNoTracking().Where(item => item.StorageKind == "Local")
                .OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
            if (file is null)
            {
                return moved;
            }

            var temporary = Path.Combine(Path.GetTempPath(), "approval-migration-" + Guid.NewGuid().ToString("N"));
            await using var buffer = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 80 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await local.CopyToAsync(file.StorageKey, buffer, cancellationToken);
            buffer.Position = 0;
            if (buffer.Length != file.Size ||
                !string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, cancellationToken)),
                    file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"附件 {file.Id} 的本地内容与元数据不一致。");
            }

            buffer.Position = 0;
            await remote.WriteAsync(file.StorageKey, buffer, file.Size, file.ContentType, cancellationToken);
            try
            {
                // 条件更新确保重复运行或并行迁移不会把已变更的元数据覆盖。
                var updated = await context.StoredFiles.Where(item => item.Id == file.Id &&
                        item.StorageKind == "Local" && item.StorageKey == file.StorageKey &&
                        item.CleanupOwner == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.StorageKind, "Minio"),
                        cancellationToken);
                if (updated != 1)
                {
                    throw new DbUpdateConcurrencyException("附件迁移时元数据已经变化。");
                }
            }
            catch
            {
                // 并行迁移可能已经把同一对象键切到 MinIO；此时不能删除获胜者正在使用的内容。
                var switched = await context.StoredFiles.AsNoTracking().AnyAsync(item => item.Id == file.Id &&
                    item.StorageKind == "Minio" && item.StorageKey == file.StorageKey, CancellationToken.None);
                if (!switched)
                {
                    await remote.DeleteAsync(file.StorageKey, CancellationToken.None);
                }

                throw;
            }

            // 元数据已经指向新副本；本地删除失败会留下可手工清理的冗余副本，不损失下载能力。
            await local.DeleteAsync(file.StorageKey, cancellationToken);
            moved++;
        }
    }
}
