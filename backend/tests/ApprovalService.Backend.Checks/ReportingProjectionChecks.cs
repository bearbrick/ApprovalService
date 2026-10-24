using System.Text.Json;
using ApprovalService.Domain.Reporting;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 纯场景验证报表小时桶和每实例事件序号增量规划。
/// </summary>
internal static class ReportingProjectionChecks
{
    /// <summary>
    /// 执行小时边界、保密、处理时长、迟到事件和撤销重算场景。
    /// </summary>
    /// <param name="check">检查记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var hour = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var publicInstance = new ReportingInstanceFact
        {
            Id = "A",
            FlowCode = "PUBLIC",
            ClientAppCode = "ERP",
            InitiatorDepartmentId = "D1",
            StartedAt = hour.AddHours(-3),
            EndedAt = hour.AddMinutes(-30),
            Status = EngineInstanceStatuses.Approved,
        };
        var secretInstance = new ReportingInstanceFact
        {
            Id = "B",
            FlowCode = "SECRET",
            ClientAppCode = "ERP",
            InitiatorDepartmentId = "D2",
            IsConfidential = true,
            StartedAt = hour.AddHours(-3).AddMinutes(30),
            EndedAt = hour.AddMinutes(-15),
            Status = EngineInstanceStatuses.Rejected,
        };
        var futureInstance = new ReportingInstanceFact
        {
            Id = "C",
            FlowCode = "FUTURE",
            ClientAppCode = "ERP",
            StartedAt = hour,
            Status = EngineInstanceStatuses.Running,
        };
        var first = new ReportingTaskFact
        {
            Id = "T1",
            InstanceId = "A",
            NodeId = "review",
            AssigneeId = "P1",
            Status = EngineTaskStatuses.Approved,
            CompletedAt = hour.AddHours(-1).AddMinutes(-45),
            DueAt = hour.AddHours(-2),
        };
        var second = new ReportingTaskFact
        {
            Id = "T2",
            InstanceId = "A",
            NodeId = "review",
            AssigneeId = "P1",
            Status = EngineTaskStatuses.Approved,
            CompletedAt = hour.AddHours(-1).AddMinutes(-10),
            DueAt = hour.AddHours(-1).AddMinutes(-30),
        };
        var secretTask = new ReportingTaskFact
        {
            Id = "TS",
            InstanceId = "B",
            NodeId = "secret-review",
            AssigneeId = "secret-person",
            Status = EngineTaskStatuses.Approved,
            CompletedAt = hour.AddHours(-1).AddMinutes(-40),
            DueAt = hour.AddHours(-2),
        };
        var futureTask = new ReportingTaskFact
        {
            Id = "TF",
            InstanceId = "C",
            NodeId = "future",
            AssigneeId = "P2",
            Status = EngineTaskStatuses.Pending,
            DueAt = hour,
        };
        var facts = new ReportingFacts
        {
            Instances = [publicInstance, secretInstance, futureInstance],
            Tasks = [first, second, secretTask, futureTask],
            TaskCreatedEvents =
            [
                new ReportingTaskCreatedFact { TaskId = "T1", InstanceId = "A", OccurredAt = hour.AddHours(-2).AddMinutes(-30) },
                new ReportingTaskCreatedFact { TaskId = "T2", InstanceId = "A", OccurredAt = hour.AddHours(-2) },
            ],
            TaskActivatedEvents =
            [
                new ReportingTaskActivatedFact { TaskId = "T1", OccurredAt = hour.AddHours(-1).AddMinutes(-55) },
            ],
        };
        var buckets = ReportingHourBucketCalculator.Calculate(facts, hour, hour.AddHours(1));
        check("报表按 UTC 整点分桶且排除尚未关闭小时",
            buckets.All(item => item.HourUtc < hour && item.HourUtc.Minute == 0) &&
            !buckets.Any(item => item.FlowCode == "FUTURE"));
        var completed = buckets.Single(item => item.FlowCode == "PUBLIC" && item.Kind == ReportingHourMetricKinds.Completed);
        check("审批单结束量和真实耗时进入结束小时并保留精确样本",
            completed.HourUtc == hour.AddHours(-1) && completed.Count == 1 &&
            completed.ApprovedCount == 1 && completed.DurationSamplesMinutes.SequenceEqual([150d]));
        var taskCompleted = buckets.Single(item => item.Kind == ReportingHourMetricKinds.TaskCompleted);
        check("同小时同节点和人员合桶，排队任务从激活时计时并排序耗时样本",
            taskCompleted.Count == 2 && taskCompleted.NodeId == "review" && taskCompleted.AssigneeId == "P1" &&
            taskCompleted.DurationSamplesMinutes.SequenceEqual([10d, 50d]));
        var due = buckets.Where(item => item.Kind == ReportingHourMetricKinds.TaskDue).ToArray();
        check("已到期任务独立成桶并使用已确认的逾期分母口径",
            due.Sum(item => item.Count) == 2 && due.Sum(item => item.OverdueCount) == 2);
        check("保密流程只产生匿名发起和结束桶，不含节点人员和时长样本",
            buckets.Where(item => item.IsConfidential).Select(item => item.Kind)
                .Order(StringComparer.Ordinal).SequenceEqual([ReportingHourMetricKinds.Completed, ReportingHourMetricKinds.Started]) &&
            buckets.Where(item => item.IsConfidential).All(item =>
                item.NodeId is null && item.AssigneeId is null && item.DurationSamplesMinutes.Count == 0));
        check("相同事实和固定观察时间重复计算完全一致",
            JsonSerializer.Serialize(buckets) == JsonSerializer.Serialize(ReportingHourBucketCalculator.Calculate(facts, hour, hour.AddHours(1))));

        var sources = new[]
        {
            new ReportingProjectionSource { InstanceId = "A", LastEventSequence = 2, MaxEventOccurredAtUtc = hour.AddMinutes(-20) },
            new ReportingProjectionSource { InstanceId = "B", LastEventSequence = 3, MaxEventOccurredAtUtc = hour.AddMinutes(-10) },
            new ReportingProjectionSource { InstanceId = "C", LastEventSequence = 1, MaxEventOccurredAtUtc = hour },
            new ReportingProjectionSource { InstanceId = "D", LastEventSequence = 0 },
            new ReportingProjectionSource { InstanceId = "E", LastEventSequence = 5, MaxEventOccurredAtUtc = hour.AddHours(-2) },
        };
        var saved = new[]
        {
            new ReportingProjectionCheckpoint { InstanceId = "A", LastEventSequence = 2 },
            new ReportingProjectionCheckpoint { InstanceId = "B", LastEventSequence = 1 },
            new ReportingProjectionCheckpoint { InstanceId = "E", LastEventSequence = 4 },
        };
        var plan = ReportingProjectionPlanner.Plan(sources, saved, hour);
        check("事件序号水位只选首次与变更实例，迟到旧时间事件仍被发现",
            plan.Replace.Select(item => item.InstanceId).SequenceEqual(["B", "D", "E"]) &&
            plan.Replace.Single(item => item.InstanceId == "E").LastEventSequence == 5);
        check("尚未关闭小时的事件不推进水位", plan.DeferredInstanceIds.SequenceEqual(["C"]));
        var advanced = sources.Where(item => item.InstanceId != "C")
            .Select(item => new ReportingProjectionCheckpoint
            {
                InstanceId = item.InstanceId,
                LastEventSequence = item.LastEventSequence,
            }).ToArray();
        check("水位保存后重跑没有重复替换",
            ReportingProjectionPlanner.Plan(sources, advanced, hour).Replace.Count == 0);

        first.Status = EngineTaskStatuses.Pending;
        first.CompletedAt = null;
        var afterRevocation = ReportingHourBucketCalculator.Calculate(facts, hour, hour.AddHours(1));
        var revocationPlan = ReportingProjectionPlanner.Plan(
            [new ReportingProjectionSource { InstanceId = "A", LastEventSequence = 3, MaxEventOccurredAtUtc = hour.AddMinutes(-5) }],
            [new ReportingProjectionCheckpoint { InstanceId = "A", LastEventSequence = 2 }], hour);
        check("撤销任务后按更高事件序号整实例替换旧桶，不累计过期完成量",
            revocationPlan.Replace.Single().InstanceId == "A" &&
            afterRevocation.Single(item => item.Kind == ReportingHourMetricKinds.TaskCompleted).Count == 1);
        var rejectedRegression = false;
        try
        {
            ReportingProjectionPlanner.Plan(
                [new ReportingProjectionSource { InstanceId = "A", LastEventSequence = 1, MaxEventOccurredAtUtc = hour.AddHours(-1) }],
                [new ReportingProjectionCheckpoint { InstanceId = "A", LastEventSequence = 2 }], hour);
        }
        catch (InvalidDataException)
        {
            rejectedRegression = true;
        }

        check("投影拒绝事件序号回退", rejectedRegression);
    }
}
