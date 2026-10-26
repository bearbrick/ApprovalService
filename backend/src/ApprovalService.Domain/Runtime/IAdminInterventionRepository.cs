namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 只读取当前管理员有权干预的运行快照，不向网页泄露其他单据。
/// </summary>
public interface IAdminInterventionRepository
{
    /// <summary>
    /// 在当前组织和参与关系下读取最近一百张在途单据，或指定单据。
    /// </summary>
    /// <param name="actorId">可信内部人员 ID。</param>
    /// <param name="isLocal">是否本地账号会话。</param>
    /// <param name="instanceId">指定单据；空值读取最近列表。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已授权的运行快照。</returns>
    Task<IReadOnlyList<RuntimeSnapshot>> ReadAsync(string actorId, bool isLocal, string? instanceId,
        CancellationToken cancellationToken = default);
}
