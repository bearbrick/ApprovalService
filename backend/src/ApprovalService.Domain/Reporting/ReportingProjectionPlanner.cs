namespace ApprovalService.Domain.Reporting;

/// <summary>
/// 按审批单事件序号规划幂等重投影；不读取或写入数据库。
/// </summary>
public static class ReportingProjectionPlanner
{
    /// <summary>
    /// 对已经关闭的 UTC 小时选择首次或发生变更的审批单。
    /// </summary>
    /// <param name="sources">当前审批单及已提交事件的最大发生时间。</param>
    /// <param name="checkpoints">已持久化的每审批单事件序号水位。</param>
    /// <param name="closedUntilUtc">第一个未关闭小时的 UTC 起点。</param>
    /// <returns>需要替换和因未来事件延后的审批单；未变化者不出现。</returns>
    public static ReportingProjectionPlan Plan(IReadOnlyList<ReportingProjectionSource> sources,
        IReadOnlyList<ReportingProjectionCheckpoint> checkpoints, DateTimeOffset closedUntilUtc)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(checkpoints);
        if (closedUntilUtc == default || closedUntilUtc.Offset != TimeSpan.Zero ||
            closedUntilUtc.Ticks % TimeSpan.TicksPerHour != 0)
        {
            throw new ArgumentException("投影截止时间必须是 UTC 整点小时。", nameof(closedUntilUtc));
        }

        var saved = checkpoints.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var replace = new List<ReportingProjectionReplacement>();
        var deferred = new List<string>();
        foreach (var source in sources.OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(source.InstanceId) || !seen.Add(source.InstanceId) ||
                source.LastEventSequence < 0 ||
                (source.LastEventSequence > 0 && source.MaxEventOccurredAtUtc is null) ||
                (source.LastEventSequence == 0 && source.MaxEventOccurredAtUtc is not null) ||
                (source.MaxEventOccurredAtUtc is { } occurredAt && occurredAt.Offset != TimeSpan.Zero))
            {
                throw new InvalidDataException("报表投影来源的审批单或事件水位无效。");
            }

            var hasSaved = saved.TryGetValue(source.InstanceId, out var checkpoint);
            if (hasSaved && checkpoint!.LastEventSequence > source.LastEventSequence)
            {
                // 事件序号只可前进；倒退意味着来源投影或操作记录被改写。
                throw new InvalidDataException("报表投影来源事件序号发生回退。");
            }

            if (hasSaved && checkpoint!.LastEventSequence == source.LastEventSequence)
            {
                continue;
            }

            // 不推进水位越过未关闭小时；下一轮仍会看到这个审批单。
            if (source.MaxEventOccurredAtUtc >= closedUntilUtc)
            {
                deferred.Add(source.InstanceId);
                continue;
            }

            replace.Add(new ReportingProjectionReplacement
            {
                InstanceId = source.InstanceId,
                LastEventSequence = source.LastEventSequence,
            });
        }

        return new ReportingProjectionPlan
        {
            Replace = replace,
            DeferredInstanceIds = deferred,
        };
    }
}
