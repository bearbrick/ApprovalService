namespace ApprovalService.Domain.Center;

/// <summary>
/// 审批中心只读仓储；授权必须在 SQL 过滤、计数和分页前执行。
/// </summary>
public interface ICenterQueryRepository
{
    /// <summary>
    /// 查询当前人员的一页审批单及其总数。
    /// </summary>
    /// <param name="scope">已验证的人员与嵌入应用范围。</param>
    /// <param name="query">分页和筛选条件。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已授权分页快照。</returns>
    Task<CenterPage> ListAsync(CenterScope scope, CenterQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在相同的人员与应用可见范围内读取详情。
    /// </summary>
    /// <param name="scope">已验证的人员与嵌入应用范围。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已授权记录；不存在或不可见时统一为空。</returns>
    Task<CenterRecord?> FindAsync(CenterScope scope, string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重新核对参与关系和嵌入应用范围后，幂等推进本人的审批单已读游标。
    /// </summary>
    /// <param name="scope">已验证的人员与嵌入应用范围。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="seenThroughSequence">详情响应中实际显示的活动序号。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>当前已读事件序号；单据不可见时为空。</returns>
    Task<long?> MarkReadAsync(CenterScope scope, string instanceId, long seenThroughSequence,
        CancellationToken cancellationToken = default);
}
