namespace ApprovalService.Domain.Center;

/// <summary>
/// 委托规则的持久接口，写入时重新校验同一委托人的时间和流程交集。
/// </summary>
public interface IDelegationRuleRepository
{
    /// <summary>
    /// 列出指定委托人的规则。
    /// </summary>
    /// <param name="principalId">内部人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>按生效时间排序的规则。</returns>
    Task<IReadOnlyList<DelegationRuleEntity>> ListAsync(string principalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在事务中检查时间和流程交集并新增规则。
    /// </summary>
    /// <param name="rule">已完成基本校验的规则。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    Task CreateAsync(DelegationRuleEntity rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除一项属于指定委托人的规则，已有任务不受影响。
    /// </summary>
    /// <param name="principalId">内部人员 ID。</param>
    /// <param name="id">规则 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>是否找到规则。</returns>
    Task<bool> DeleteAsync(string principalId, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载当前时刻和流程上有效的一层委托快照。
    /// </summary>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="occurredAt">固定的操作时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>有效规则。</returns>
    Task<IReadOnlyList<DelegationRuleEntity>> ListActiveAsync(string flowCode, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
