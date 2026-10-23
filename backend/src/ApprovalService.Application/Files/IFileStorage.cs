namespace ApprovalService.Application.Files;

/// <summary>
/// 附件内容的存储扩展点；数据库元数据由独立仓储保存。
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// 存储实现的稳定编码。
    /// </summary>
    string Kind
    {
        get;
    }

    /// <summary>
    /// 把已校验的文件字节写入指定对象键。
    /// </summary>
    /// <param name="key">服务生成的安全对象键。</param>
    /// <param name="source">从零位置读取的文件流。</param>
    /// <param name="length">已校验的字节数。</param>
    /// <param name="contentType">按文件内容识别的媒体类型。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task WriteAsync(string key, Stream source, long length, string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将对象内容复制到已经通过权限检查的输出流。
    /// </summary>
    /// <param name="key">对象键。</param>
    /// <param name="destination">输出流。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task CopyToAsync(string key, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除对象；对象已不存在时也视为成功。
    /// </summary>
    /// <param name="key">对象键。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 若浏览器可以直接访问存储服务，生成短期下载地址。
    /// </summary>
    /// <param name="key">对象键。</param>
    /// <param name="lifetime">地址有效期。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>可直接访问的地址；需要服务转发时为空。</returns>
    Task<Uri?> CreateDownloadUrlAsync(string key, TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 按新上传默认存储和历史元数据中的编码选择存储实现。
/// </summary>
public interface IFileStorageSelector
{
    /// <summary>
    /// 当前新上传文件使用的存储实现。
    /// </summary>
    IFileStorage Current
    {
        get;
    }

    /// <summary>
    /// 解析历史文件所在的存储实现。
    /// </summary>
    /// <param name="kind">元数据中的存储编码。</param>
    /// <returns>匹配的存储实现。</returns>
    IFileStorage Resolve(string kind);
}
