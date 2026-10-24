using ApprovalService.Engine.Execution;

namespace ApprovalService.Domain.Reporting;

/// <summary>
/// 从真实最小化事实计算内部报表指标；不执行权限判断或数据库读取。
/// </summary>
public static class ReportingMetricsCalculator
{
    /// <summary>
    /// 按已确认的双时间口径、发起部门快照和到期任务分母计算指标。
    /// </summary>
    /// <param name="facts">覆盖筛选范围及当前待办的完整事实集合。</param>
    /// <param name="query">筛选与观察时间。</param>
    /// <returns>流程、节点和实际处理人指标。</returns>
    public static ReportingMetrics Calculate(ReportingFacts facts, ReportingMetricsQuery query)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(query);
        if (query.FromUtc.Offset != TimeSpan.Zero || query.UntilUtc.Offset != TimeSpan.Zero ||
            query.AsOfUtc.Offset != TimeSpan.Zero || query.FromUtc >= query.UntilUtc)
        {
            throw new ArgumentException("报表时间范围必须是非空的 UTC 半开区间，观察时间也必须为 UTC。", nameof(query));
        }

        // 部门只用审批单发起时锁定的快照，避免组织调动改变历史报表。
        var instances = facts.Instances.Where(item =>
            (query.FlowCode is null || item.FlowCode == query.FlowCode) &&
            (query.ClientAppCode is null || item.ClientAppCode == query.ClientAppCode) &&
            (query.InitiatorDepartmentId is null || item.InitiatorDepartmentId == query.InitiatorDepartmentId)).ToArray();
        var instanceById = instances.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var tasks = facts.Tasks.Where(item => instanceById.ContainsKey(item.InstanceId)).ToArray();
        var started = instances.Where(item => InWindow(item.StartedAt, query)).ToArray();
        var ended = instances.Where(item => InWindow(item.EndedAt, query)).ToArray();
        var completed = tasks.Where(item => InWindow(item.CompletedAt, query)).ToArray();
        var due = tasks.Where(item => item.DueAt <= query.AsOfUtc && InWindow(item.DueAt, query)).ToArray();
        var pending = tasks.Where(item => item.Status == EngineTaskStatuses.Pending && item.CompletedAt is null).ToArray();

        // 依次审批的排队任务以激活时间为起点，其余任务以创建事件为起点。
        var createdAt = facts.TaskCreatedEvents.Where(item => instanceById.ContainsKey(item.InstanceId))
            .GroupBy(item => item.TaskId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(item => item.OccurredAt), StringComparer.Ordinal);
        var activatedAt = facts.TaskActivatedEvents
            .GroupBy(item => item.TaskId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(item => item.OccurredAt), StringComparer.Ordinal);
        var durations = completed.ToDictionary(item => item.Id,
            item => DurationMinutes(item, createdAt, activatedAt), StringComparer.Ordinal);

        var flows = started.Select(item => item.FlowCode).Concat(ended.Select(item => item.FlowCode))
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal)
            .Select(code => Flow(code, started, ended)).ToArray();
        var nodes = completed.Concat(due).DistinctBy(item => item.Id)
            .GroupBy(item => (instanceById[item.InstanceId].FlowCode, item.NodeId))
            .OrderBy(group => group.Key.FlowCode, StringComparer.Ordinal).ThenBy(group => group.Key.NodeId, StringComparer.Ordinal)
            .Select(group => Node(group.Key.FlowCode, group.Key.NodeId,
                group.Select(item => item.Id).ToHashSet(StringComparer.Ordinal), completed, due, durations, query.AsOfUtc)).ToArray();
        var people = completed.Concat(due).Concat(pending).DistinctBy(item => item.Id)
            .GroupBy(item => item.AssigneeId, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Person(group.Key, completed, due, pending, durations, query.AsOfUtc)).ToArray();
        return new ReportingMetrics { Flows = flows, Nodes = nodes, People = people };
    }

    /// <summary>
    /// 判断时间是否落在 UTC 半开区间。
    /// </summary>
    /// <param name="value">待判断时间。</param>
    /// <param name="query">时间范围。</param>
    /// <returns>落在区间内时为真。</returns>
    private static bool InWindow(DateTimeOffset? value, ReportingMetricsQuery query) =>
        value >= query.FromUtc && value < query.UntilUtc;

    /// <summary>
    /// 汇总一个流程的发起、完成和历时。
    /// </summary>
    /// <param name="code">流程编码。</param>
    /// <param name="started">在时间段发起的审批单。</param>
    /// <param name="ended">在时间段结束的审批单。</param>
    /// <returns>流程指标。</returns>
    private static ReportingFlowMetric Flow(string code, ReportingInstanceFact[] started, ReportingInstanceFact[] ended)
    {
        var finished = ended.Where(item => item.FlowCode == code).ToArray();
        var minutes = finished.Where(item => item.EndedAt >= item.StartedAt)
            .Select(item => (item.EndedAt!.Value - item.StartedAt).TotalMinutes).Order().ToArray();
        return new ReportingFlowMetric
        {
            FlowCode = code,
            StartedCount = started.Count(item => item.FlowCode == code),
            CompletedCount = finished.Length,
            ApprovedCount = finished.Count(item => item.Status == EngineInstanceStatuses.Approved),
            RejectedCount = finished.Count(item => item.Status == EngineInstanceStatuses.Rejected),
            WithdrawnCount = finished.Count(item => item.Status == EngineInstanceStatuses.Withdrawn),
            AverageDurationMinutes = Mean(minutes),
            P90DurationMinutes = minutes.Length == 0 ? null : minutes[(int)Math.Ceiling(minutes.Length * 0.9) - 1],
        };
    }

    /// <summary>
    /// 汇总一个流程节点的处理历时和逾期。
    /// </summary>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="nodeId">节点 ID。</param>
    /// <param name="scopedTaskIds">该流程节点的任务 ID。</param>
    /// <param name="completed">时间段内完成的任务。</param>
    /// <param name="due">时间段内已到期的任务。</param>
    /// <param name="durations">有真实起点的任务处理历时。</param>
    /// <param name="asOfUtc">观察时间。</param>
    /// <returns>节点指标。</returns>
    private static ReportingNodeMetric Node(string flowCode, string nodeId, IReadOnlySet<string> scopedTaskIds,
        ReportingTaskFact[] completed,
        ReportingTaskFact[] due, IReadOnlyDictionary<string, double?> durations, DateTimeOffset asOfUtc)
    {
        // 不仅按节点 ID，还按所属流程任务 ID 筛选，避免不同流程同名节点混算。
        var finished = completed.Where(item => scopedTaskIds.Contains(item.Id)).ToArray();
        var expired = due.Where(item => scopedTaskIds.Contains(item.Id)).ToArray();
        return new ReportingNodeMetric
        {
            FlowCode = flowCode,
            NodeId = nodeId,
            CompletedCount = finished.Length,
            AverageDurationMinutes = Mean(finished.Select(item => durations[item.Id]).Where(item => item.HasValue).Select(item => item!.Value)),
            DueCount = expired.Length,
            OverdueCount = expired.Count(item => IsOverdue(item, asOfUtc)),
        };
    }

    /// <summary>
    /// 汇总实际处理人的处理量、待办和逾期。
    /// </summary>
    /// <param name="assigneeId">实际处理人工号。</param>
    /// <param name="completed">时间段内完成的任务。</param>
    /// <param name="due">时间段内已到期的任务。</param>
    /// <param name="pending">当前待处理任务。</param>
    /// <param name="durations">有真实起点的任务处理历时。</param>
    /// <param name="asOfUtc">观察时间。</param>
    /// <returns>人员指标。</returns>
    private static ReportingPersonMetric Person(string assigneeId, ReportingTaskFact[] completed,
        ReportingTaskFact[] due, ReportingTaskFact[] pending, IReadOnlyDictionary<string, double?> durations,
        DateTimeOffset asOfUtc)
    {
        var finished = completed.Where(item => item.AssigneeId == assigneeId).ToArray();
        return new ReportingPersonMetric
        {
            AssigneeId = assigneeId,
            PendingCount = pending.Count(item => item.AssigneeId == assigneeId),
            CompletedCount = finished.Length,
            AverageDurationMinutes = Mean(finished.Select(item => durations[item.Id]).Where(item => item.HasValue).Select(item => item!.Value)),
            OverdueCount = due.Count(item => item.AssigneeId == assigneeId && IsOverdue(item, asOfUtc)),
        };
    }

    /// <summary>
    /// 读取任务真实处理起点并计算有效历时。
    /// </summary>
    /// <param name="task">已完成任务。</param>
    /// <param name="createdAt">任务创建事件。</param>
    /// <param name="activatedAt">任务激活事件。</param>
    /// <returns>历时分钟数；缺少有效事件时为空。</returns>
    private static double? DurationMinutes(ReportingTaskFact task,
        IReadOnlyDictionary<string, DateTimeOffset> createdAt, IReadOnlyDictionary<string, DateTimeOffset> activatedAt)
    {
        if (task.CompletedAt is null)
        {
            return null;
        }

        if (!activatedAt.TryGetValue(task.Id, out var start) && !createdAt.TryGetValue(task.Id, out start))
        {
            return null;
        }

        return task.CompletedAt < start ? null : (task.CompletedAt.Value - start).TotalMinutes;
    }

    /// <summary>
    /// 识别已到期任务的逾期情况。
    /// </summary>
    /// <param name="task">已到期任务。</param>
    /// <param name="asOfUtc">观察时间。</param>
    /// <returns>逾期完成或仍待办时为真。</returns>
    private static bool IsOverdue(ReportingTaskFact task, DateTimeOffset asOfUtc) =>
        task.CompletedAt > task.DueAt ||
        (task.Status == EngineTaskStatuses.Pending && task.CompletedAt is null && task.DueAt <= asOfUtc);

    /// <summary>
    /// 计算非空样本的平均值。
    /// </summary>
    /// <param name="values">样本值。</param>
    /// <returns>样本平均值，空样本返回空值。</returns>
    private static double? Mean(IEnumerable<double> values)
    {
        var sample = values.ToArray();
        return sample.Length == 0 ? null : sample.Average();
    }
}
