namespace ApprovalService.Application.Files;

/// <summary>
/// 管理命令调用的本地到对象存储迁移入口。
/// </summary>
public interface IFileMigrationService
{
    /// <summary>
    /// 先升级数据库结构，再逐个迁移本地附件。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>完成迁移的文件数。</returns>
    Task<int> MigrateAsync(CancellationToken cancellationToken = default);
}
