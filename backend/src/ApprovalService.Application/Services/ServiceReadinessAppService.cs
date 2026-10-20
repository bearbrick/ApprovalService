using ApprovalService.Domain.Runtime;

namespace ApprovalService.Application.Services;

/// <summary>
/// 通过仓储抽象检查数据库连通性，不在接口层直接访问数据库。
/// </summary>
/// <param name="repository">检查连接的持久化仓储。</param>
public sealed class ServiceReadinessAppService(IApprovalRuntimeRepository repository) : IServiceReadinessAppService
{
    /// <inheritdoc />
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        repository.CanConnectAsync(cancellationToken);
}
