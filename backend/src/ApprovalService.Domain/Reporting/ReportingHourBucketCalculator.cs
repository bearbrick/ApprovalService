using ApprovalService.Engine.Execution;

namespace ApprovalService.Domain.Reporting;

/// <summary>
/// 从最小事实按 UTC 小时确定性分桶，供后续持久投影和趋势查询使用。
/// </summary>
public static class ReportingHourBucketCalculator
{
    /// <summary>
    /// 汇总已关闭小时；当前待办只应在查询时实时读取，不写入历史小时桶。
    /// </summary>
    /// <param name="facts">待投影审批单及任务的完整事实。</param>
    /// <param name="closedUntilUtc">第一个未关闭小时的 UTC 起点。</param>
    /// <param name="asOfUtc">到期状态的固定观察时间，不早于关闭水位。</param>
    /// <returns>按小时和维度稳定排序的内部指标。</returns>
    public static IReadOnlyList<ReportingHourBucket> Calculate(ReportingFacts facts,
        DateTimeOffset closedUntilUtc, DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ValidateClock(closedUntilUtc, asOfUtc);
        var byId = facts.Instances.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var created = facts.TaskCreatedEvents.GroupBy(item => item.TaskId, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Min(entry => entry.OccurredAt), StringComparer.Ordinal);
        var activated = facts.TaskActivatedEvents.GroupBy(item => item.TaskId, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Min(entry => entry.OccurredAt), StringComparer.Ordinal);
        var parts = new List<ReportingHourBucket>();

        foreach (var instance in facts.Instances)
        {
            RequireUtc(instance.StartedAt);
            if (instance.StartedAt < closedUntilUtc)
            {
                parts.Add(InstancePart(instance, instance.StartedAt, ReportingHourMetricKinds.Started));
            }

            if (instance.EndedAt is { } endedAt)
            {
                RequireUtc(endedAt);
                if (endedAt < closedUntilUtc)
                {
                    parts.Add(InstancePart(instance, endedAt, ReportingHourMetricKinds.Completed));
                }
            }
        }

        foreach (var task in facts.Tasks)
        {
            if (!byId.TryGetValue(task.InstanceId, out var instance))
            {
                throw new InvalidDataException("报表任务缺少所属审批单，不能生成小时桶。");
            }

            // 保密流程的任务、节点和人员维度不得进入汇总；只保留审批单匿名数量。
            if (instance.IsConfidential)
            {
                continue;
            }

            if (task.CompletedAt is { } completedAt)
            {
                RequireUtc(completedAt);
                if (completedAt < closedUntilUtc)
                {
                    DateTimeOffset? createdAt = created.TryGetValue(task.Id, out var createdValue) ? createdValue : null;
                    DateTimeOffset? activatedAt = activated.TryGetValue(task.Id, out var activatedValue) ? activatedValue : null;
                    var duration = ReportingTaskMetricRules.TaskMinutes(completedAt, createdAt, activatedAt);
                    parts.Add(TaskPart(instance, task, completedAt, ReportingHourMetricKinds.TaskCompleted,
                        duration, false));
                }
            }

            if (task.DueAt is { } dueAt)
            {
                RequireUtc(dueAt);
                if (dueAt < closedUntilUtc)
                {
                    parts.Add(TaskPart(instance, task, dueAt, ReportingHourMetricKinds.TaskDue, null,
                        ReportingTaskMetricRules.IsOverdue(task, asOfUtc)));
                }
            }
        }

        // 时长样本单独保留，跨小时合并后仍可精确算最近秩 P90。
        return parts.GroupBy(item => (item.HourUtc, item.Kind, item.FlowCode, item.ClientAppCode,
                item.InitiatorDepartmentId, item.IsConfidential, item.NodeId, item.AssigneeId))
            .Select(group => new ReportingHourBucket
            {
                HourUtc = group.Key.HourUtc,
                Kind = group.Key.Kind,
                FlowCode = group.Key.FlowCode,
                ClientAppCode = group.Key.ClientAppCode,
                InitiatorDepartmentId = group.Key.InitiatorDepartmentId,
                IsConfidential = group.Key.IsConfidential,
                NodeId = group.Key.NodeId,
                AssigneeId = group.Key.AssigneeId,
                Count = group.Sum(item => item.Count),
                ApprovedCount = group.Sum(item => item.ApprovedCount),
                RejectedCount = group.Sum(item => item.RejectedCount),
                WithdrawnCount = group.Sum(item => item.WithdrawnCount),
                OverdueCount = group.Sum(item => item.OverdueCount),
                DurationSamplesMinutes = group.SelectMany(item => item.DurationSamplesMinutes).Order().ToArray(),
            })
            .OrderBy(item => item.HourUtc).ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.FlowCode, StringComparer.Ordinal)
            .ThenBy(item => item.ClientAppCode, StringComparer.Ordinal)
            .ThenBy(item => item.InitiatorDepartmentId, StringComparer.Ordinal)
            .ThenBy(item => item.NodeId, StringComparer.Ordinal)
            .ThenBy(item => item.AssigneeId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 生成审批单发起或结束的一条待分组事实。
    /// </summary>
    /// <param name="instance">审批单事实。</param>
    /// <param name="atUtc">发起或结束时间。</param>
    /// <param name="kind">发起或结束指标种类。</param>
    /// <returns>只含统计字段的单项指标。</returns>
    private static ReportingHourBucket InstancePart(ReportingInstanceFact instance, DateTimeOffset atUtc, string kind)
    {
        var ended = kind == ReportingHourMetricKinds.Completed;
        var duration = ended && !instance.IsConfidential && instance.EndedAt >= instance.StartedAt
            ? (instance.EndedAt!.Value - instance.StartedAt).TotalMinutes : (double?)null;
        return new ReportingHourBucket
        {
            HourUtc = HourStart(atUtc),
            Kind = kind,
            FlowCode = instance.FlowCode,
            ClientAppCode = instance.ClientAppCode,
            InitiatorDepartmentId = instance.InitiatorDepartmentId,
            IsConfidential = instance.IsConfidential,
            Count = 1,
            ApprovedCount = ended && !instance.IsConfidential && instance.Status == EngineInstanceStatuses.Approved ? 1 : 0,
            RejectedCount = ended && !instance.IsConfidential && instance.Status == EngineInstanceStatuses.Rejected ? 1 : 0,
            WithdrawnCount = ended && !instance.IsConfidential && instance.Status == EngineInstanceStatuses.Withdrawn ? 1 : 0,
            DurationSamplesMinutes = duration is { } value ? [value] : [],
        };
    }

    /// <summary>
    /// 生成非保密任务完成或到期的一条待分组事实。
    /// </summary>
    /// <param name="instance">所属审批单。</param>
    /// <param name="task">任务事实。</param>
    /// <param name="atUtc">完成或到期时间。</param>
    /// <param name="kind">任务指标种类。</param>
    /// <param name="duration">有真实处理起点的时长。</param>
    /// <param name="overdue">已到期任务是否逾期。</param>
    /// <returns>只含统计字段的单项指标。</returns>
    private static ReportingHourBucket TaskPart(ReportingInstanceFact instance, ReportingTaskFact task,
        DateTimeOffset atUtc, string kind, double? duration, bool overdue) => new()
        {
            HourUtc = HourStart(atUtc),
            Kind = kind,
            FlowCode = instance.FlowCode,
            ClientAppCode = instance.ClientAppCode,
            InitiatorDepartmentId = instance.InitiatorDepartmentId,
            NodeId = task.NodeId,
            AssigneeId = task.AssigneeId,
            Count = 1,
            OverdueCount = overdue ? 1 : 0,
            DurationSamplesMinutes = duration is { } value ? [value] : [],
        };

    /// <summary>
    /// 计算 UTC 整点小时起点。
    /// </summary>
    /// <param name="atUtc">UTC 事实时间。</param>
    /// <returns>所属小时的 UTC 起点。</returns>
    private static DateTimeOffset HourStart(DateTimeOffset atUtc) =>
        new(atUtc.Year, atUtc.Month, atUtc.Day, atUtc.Hour, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 核对关闭水位与观察时间。
    /// </summary>
    /// <param name="closedUntilUtc">第一个未关闭小时。</param>
    /// <param name="asOfUtc">固定观察时间。</param>
    private static void ValidateClock(DateTimeOffset closedUntilUtc, DateTimeOffset asOfUtc)
    {
        RequireUtc(closedUntilUtc);
        RequireUtc(asOfUtc);
        if (closedUntilUtc == default || closedUntilUtc.Ticks % TimeSpan.TicksPerHour != 0 || asOfUtc < closedUntilUtc)
        {
            throw new ArgumentException("小时汇总截止时间必须是已关闭的 UTC 整点小时。");
        }
    }

    /// <summary>
    /// 事实时间必须为 UTC，避免混入其他时区的小时桶。
    /// </summary>
    /// <param name="value">待检查时间。</param>
    private static void RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("报表小时桶只接受 UTC 时间。");
        }
    }
}
