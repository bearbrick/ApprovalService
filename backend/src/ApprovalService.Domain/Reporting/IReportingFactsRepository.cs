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

    /// <summary>
    /// 读取报表筛选期内发起、结束、处理或到期以及当前待办的最小化事实。
    /// </summary>
    /// <param name="query">双时间口径和管理范围筛选；当前待办使用现时任务投影。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>覆盖指标计算所需审批单及任务的事实集合。</returns>
    Task<ReportingFacts> ReadForMetricsAsync(ReportingMetricsQuery query, CancellationToken cancellationToken = default);
}
