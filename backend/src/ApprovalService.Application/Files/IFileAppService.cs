namespace ApprovalService.Application.Files;

/// <summary>
/// 附件上传、关联、验权下载与孤儿清理的应用入口。
/// </summary>
public interface IFileAppService
{
    /// <summary>
    /// 校验并上传一份附件。
    /// </summary>
    /// <param name="input">文件流、显示名和可信上传身份。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>持久化后的附件信息。</returns>
    Task<StoredFileDto> UploadAsync(UploadFileInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将已经上传的附件关联到可见的审批单。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="userId">已认证调用者 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task LinkAsync(string fileId, string instanceId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查查看权限后，把文件转发到输出流或签发短时直连地址。
    /// </summary>
    /// <param name="fileId">文件 ID。</param>
    /// <param name="userId">已认证调用者 ID。</param>
    /// <param name="destination">需要转发时使用的输出流。</param>
    /// <param name="clientAppCode">嵌入会话绑定的接入应用编码；本地会话为空。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>下载响应信息。</returns>
    Task<FileDownloadDto> DownloadAsync(string fileId, string userId, Stream destination,
        string? clientAppCode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除超过保留期且未被任何审批单使用的附件。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本轮完成清理的文件数。</returns>
    Task<int> CleanupUnusedAsync(CancellationToken cancellationToken = default);
}
