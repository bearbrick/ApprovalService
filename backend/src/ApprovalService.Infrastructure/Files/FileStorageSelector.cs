using ApprovalService.Application.Files;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Files;

/// <summary>
/// 当前上传按配置选择存储，历史附件按元数据中的实现编码读取。
/// </summary>
/// <param name="storages">已登记的存储实现。</param>
/// <param name="options">新上传文件的提供者配置。</param>
public sealed class FileStorageSelector(IEnumerable<IFileStorage> storages,
    IOptions<FileStorageOptions> options) : IFileStorageSelector
{
    /// <inheritdoc />
    public IFileStorage Current => Resolve(options.Value.Provider);

    /// <inheritdoc />
    public IFileStorage Resolve(string kind)
    {
        return storages.SingleOrDefault(item => string.Equals(item.Kind, kind, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("附件存储提供者未登记。");
    }
}
