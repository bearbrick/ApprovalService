namespace ApprovalService.Domain.Files;

/// <summary>
/// 附件元数据及审批单可见性的持久化抽象。
/// </summary>
public interface IStoredFileRepository
{
    /// <summary>
    /// 保存一条新上传文件的元数据。
    /// </summary>
    /// <param name="file">已写入外部存储的附件。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task AddAsync(StoredFileEntity file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 ID 读取附件元数据。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>文件不存在时为空。</returns>
    Task<StoredFileEntity?> FindAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将尚未清理的上传文件关联到审批单。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="uploaderId">已认证上传人 ID。</param>
    /// <param name="now">关联时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>首次关联或已关联同一审批单为真。</returns>
    Task<bool> LinkAsync(string fileId, string instanceId, string uploaderId, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户是否为审批单发起人或已分配任务的处理人。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="userId">已认证用户 ID。</param>
    /// <param name="clientAppCode">嵌入会话绑定的接入应用编码；本地会话为空。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>当前可见为真。</returns>
    Task<bool> CanViewInstanceAsync(string instanceId, string userId, string? clientAppCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出超过保留期且未关联的附件候选。
    /// </summary>
    /// <param name="cutoff">上传时间截止值。</param>
    /// <param name="now">检查租约的当前时间。</param>
    /// <param name="limit">单轮最多读取数。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>候选附件。</returns>
    Task<IReadOnlyList<StoredFileEntity>> FindUnusedAsync(DateTimeOffset cutoff, DateTimeOffset now, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 抢占过期孤儿文件的清理租约。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="cutoff">上传时间截止值。</param>
    /// <param name="now">当前时间。</param>
    /// <param name="owner">本次清理的唯一标记。</param>
    /// <param name="leaseUntil">租约失效时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>抢占成功为真。</returns>
    Task<bool> ClaimCleanupAsync(string fileId, DateTimeOffset cutoff, DateTimeOffset now, string owner,
        DateTimeOffset leaseUntil, CancellationToken cancellationToken = default);

    /// <summary>
    /// 文件内容已删除后，移除仍属于本次清理租约的元数据。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="owner">本次清理的唯一标记。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>确实删除一条元数据为真。</returns>
    Task<bool> DeleteClaimedAsync(string fileId, string owner, CancellationToken cancellationToken = default);
}
