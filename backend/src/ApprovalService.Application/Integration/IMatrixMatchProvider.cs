using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 管理端矩阵配置的可信查询入口；开放请求不能携带自造的矩阵命中结果。
/// </summary>
public interface IMatrixMatchProvider
{
    /// <summary>
    /// 按已保存的矩阵规则和审批数据求出内部人员 ID。
    /// </summary>
    /// <param name="matrixCode">已发布节点的矩阵编码。</param>
    /// <param name="initiatorId">已核实的内部发起人 ID。</param>
    /// <param name="data">已校验、已计算的审批数据快照。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本次可信命中人员 ID。</returns>
    Task<IReadOnlyList<string>> MatchAsync(string matrixCode, string initiatorId, JsonElement data,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 在管理端矩阵配置接入前给出明确的可纠正错误。
/// </summary>
public sealed class UnconfiguredMatrixMatchProvider : IMatrixMatchProvider
{
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> MatchAsync(string matrixCode, string initiatorId, JsonElement data,
        CancellationToken cancellationToken = default) =>
        throw new UserFriendlyException(ErrorCodes.ValidationFailed,
            $"审批矩阵 {matrixCode} 尚未配置，请管理员先维护审批矩阵规则。");
}
