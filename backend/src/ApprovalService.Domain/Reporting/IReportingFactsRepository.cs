namespace ApprovalService.Domain.Reporting;

/// <summary>
/// 从可信持久化状态读取统计原始事实。
/// </summary>
public interface IReportingFactsRepository
{
    /// <summary>
    /// 按明确的审批单发起时间及快照字段读取事实。
    /// </summary>
    /// <param name="query">筛选条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审批单、任务及任务创建事件事实。</returns>
    Task<ReportingFacts> ReadAsync(ReportingFactsQuery query, CancellationToken cancellationToken = default);
}
