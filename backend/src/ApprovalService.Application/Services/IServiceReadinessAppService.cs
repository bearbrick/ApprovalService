namespace ApprovalService.Application.Services;

/// <summary>
/// 服务就绪探针的应用层入口。
/// </summary>
public interface IServiceReadinessAppService
{
    /// <summary>
    /// 检查持久化依赖是否可用。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>可以接收请求为真。</returns>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
