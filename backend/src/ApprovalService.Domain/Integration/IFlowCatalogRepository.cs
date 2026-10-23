using ApprovalService.Domain.Runtime;

namespace ApprovalService.Domain.Integration;

/// <summary>
/// 开放接口读取已发布流程及其不可变版本的仓储接口。
/// </summary>
public interface IFlowCatalogRepository
{
    /// <summary>
    /// 列出全部已发布流程与各自当前版本。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已发布流程快照。</returns>
    Task<IReadOnlyList<PublishedFlowSnapshot>> ListPublishedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按流程编码读取当前发布版本。
    /// </summary>
    /// <param name="code">流程编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>流程快照；未发布时为空。</returns>
    Task<PublishedFlowSnapshot?> LoadPublishedAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>
/// 可编辑流程元数据及当前发布的不可变版本。
/// </summary>
public sealed class PublishedFlowSnapshot
{
    /// <summary>
    /// 流程元数据。
    /// </summary>
    public FlowEntity Flow { get; set; } = new();

    /// <summary>
    /// 当前发布版本。
    /// </summary>
    public FlowVersionEntity Version { get; set; } = new();
}
