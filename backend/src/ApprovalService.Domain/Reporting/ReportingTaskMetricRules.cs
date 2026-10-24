using ApprovalService.Engine.Execution;

namespace ApprovalService.Domain.Reporting;

/// <summary>
/// 报表任务处理时长共用的纯规则；排队任务从真正激活时计时。
/// </summary>
public static class ReportingTaskMetricRules
{
    /// <summary>
    /// 根据任务完成、创建和激活时间计算真实处理分钟数。
    /// </summary>
    /// <param name="completedAt">任务完成时间。</param>
    /// <param name="createdAt">任务创建事件时间。</param>
    /// <param name="activatedAt">排队任务激活事件时间；有值时优先使用。</param>
    /// <returns>可信时长，缺少起点或时间倒置时为空。</returns>
    public static double? TaskMinutes(DateTimeOffset? completedAt, DateTimeOffset? createdAt,
        DateTimeOffset? activatedAt)
    {
        var startedAt = activatedAt ?? createdAt;
        if (completedAt is null || startedAt is null || completedAt < startedAt)
        {
            return null;
        }

        return (completedAt.Value - startedAt.Value).TotalMinutes;
    }

    /// <summary>
    /// 判断已到期任务是否逾期完成或在观察时仍未处理。
    /// </summary>
    /// <param name="task">任务事实。</param>
    /// <param name="asOfUtc">固定观察时间。</param>
    /// <returns>逾期时为真。</returns>
    public static bool IsOverdue(ReportingTaskFact task, DateTimeOffset asOfUtc) =>
        task.CompletedAt > task.DueAt ||
        (task.Status == EngineTaskStatuses.Pending && task.CompletedAt is null && task.DueAt <= asOfUtc);
}
