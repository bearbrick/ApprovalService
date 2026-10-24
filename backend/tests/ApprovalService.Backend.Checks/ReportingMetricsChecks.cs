using ApprovalService.Domain.Reporting;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用跨期与任务激活事实核对统计指标口径。
/// </summary>
internal static class ReportingMetricsChecks
{
    /// <summary>
    /// 检查双时间口径、部门快照、处理历时和逾期分母。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var day = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var facts = new ReportingFacts
        {
            Instances =
            [
                new ReportingInstanceFact { Id = "a", FlowCode = "PURCHASE", ClientAppCode = "ERP", InitiatorDepartmentId = "D1", Status = EngineInstanceStatuses.Approved, StartedAt = day.AddHours(-14), EndedAt = day.AddHours(10) },
                new ReportingInstanceFact { Id = "b", FlowCode = "PURCHASE", ClientAppCode = "ERP", InitiatorDepartmentId = "D1", Status = EngineInstanceStatuses.Running, StartedAt = day.AddHours(9) },
                new ReportingInstanceFact { Id = "c", FlowCode = "PURCHASE", ClientAppCode = "ERP", InitiatorDepartmentId = "D1", Status = EngineInstanceStatuses.Rejected, StartedAt = day.AddHours(10), EndedAt = day.AddHours(11) },
                new ReportingInstanceFact { Id = "d", FlowCode = "EXPENSE", ClientAppCode = "FIN", InitiatorDepartmentId = "D2", Status = EngineInstanceStatuses.Approved, StartedAt = day.AddHours(10), EndedAt = day.AddHours(12) },
                new ReportingInstanceFact { Id = "e", FlowCode = "PURCHASE", ClientAppCode = "ERP", InitiatorDepartmentId = "D1", Status = EngineInstanceStatuses.Approved, StartedAt = day.AddDays(1), EndedAt = day.AddDays(1).AddHours(1) },
            ],
            Tasks =
            [
                new ReportingTaskFact { Id = "t1", InstanceId = "a", NodeId = "manager", AssigneeId = "U1", Status = EngineTaskStatuses.Approved, DueAt = day.AddHours(9.5), CompletedAt = day.AddHours(10) },
                new ReportingTaskFact { Id = "t2", InstanceId = "c", NodeId = "manager", AssigneeId = "U2", Status = EngineTaskStatuses.Approved, DueAt = day.AddHours(11), CompletedAt = day.AddHours(10.5) },
                new ReportingTaskFact { Id = "t3", InstanceId = "b", NodeId = "manager", AssigneeId = "U1", Status = EngineTaskStatuses.Pending, DueAt = day.AddHours(12) },
                new ReportingTaskFact { Id = "t4", InstanceId = "b", NodeId = "finance", AssigneeId = "U1", Status = EngineTaskStatuses.Pending, DueAt = day.AddDays(1).AddHours(1) },
                new ReportingTaskFact { Id = "t5", InstanceId = "b", NodeId = "manager", AssigneeId = "U3", Status = EngineTaskStatuses.Queued },
                new ReportingTaskFact { Id = "t6", InstanceId = "c", NodeId = "finance", AssigneeId = "U4", Status = EngineTaskStatuses.Approved, DueAt = day.AddHours(10.5), CompletedAt = day.AddHours(11) },
                new ReportingTaskFact { Id = "t7", InstanceId = "d", NodeId = "manager", AssigneeId = "U9", Status = EngineTaskStatuses.Approved, DueAt = day.AddHours(11), CompletedAt = day.AddHours(12) },
            ],
            TaskCreatedEvents =
            [
                new ReportingTaskCreatedFact { TaskId = "t1", InstanceId = "a", OccurredAt = day.AddHours(-14) },
                new ReportingTaskCreatedFact { TaskId = "t2", InstanceId = "c", OccurredAt = day.AddHours(10) },
                new ReportingTaskCreatedFact { TaskId = "t7", InstanceId = "d", OccurredAt = day.AddHours(11) },
            ],
            TaskActivatedEvents =
            [
                new ReportingTaskActivatedFact { TaskId = "t1", OccurredAt = day.AddHours(9) },
            ],
        };
        var query = new ReportingMetricsQuery
        {
            FromUtc = day,
            UntilUtc = day.AddDays(1),
            AsOfUtc = day.AddHours(18),
        };
        var result = ReportingMetricsCalculator.Calculate(facts, query);
        var purchase = result.Flows.Single(item => item.FlowCode == "PURCHASE");
        check("发起与结束采用各自时间的 UTC 半开区间，跨日前发起本日完成仍计完成",
            purchase.StartedCount == 2 && purchase.CompletedCount == 2 &&
            purchase.ApprovedCount == 1 && purchase.RejectedCount == 1);
        check("流程 P90 只从已结束审批单历时计算，保留跨日样本",
            purchase.AverageDurationMinutes == 750 && purchase.P90DurationMinutes == 1440);
        var manager = result.Nodes.Single(item => item.FlowCode == "PURCHASE" && item.NodeId == "manager");
        check("同名节点按流程隔离，已到期任务作逾期分母",
            manager.CompletedCount == 2 && manager.DueCount == 3 && manager.OverdueCount == 2 &&
            result.Nodes.Single(item => item.FlowCode == "EXPENSE" && item.NodeId == "manager").CompletedCount == 1);
        check("排队任务从激活起算，缺少创建事件不虚构处理时长",
            manager.AverageDurationMinutes == 45 &&
            result.Nodes.Single(item => item.FlowCode == "PURCHASE" && item.NodeId == "finance").AverageDurationMinutes is null);
        var u1 = result.People.Single(item => item.AssigneeId == "U1");
        check("人员只统计实际处理人，排队不算待办，未到期任务不算逾期",
            u1.PendingCount == 2 && u1.CompletedCount == 1 && u1.AverageDurationMinutes == 60 &&
            u1.OverdueCount == 2 && result.People.All(item => item.AssigneeId != "U3"));
        var scoped = ReportingMetricsCalculator.Calculate(facts, new ReportingMetricsQuery
        {
            FromUtc = day,
            UntilUtc = day.AddDays(1),
            AsOfUtc = day.AddHours(18),
            FlowCode = "PURCHASE",
            ClientAppCode = "ERP",
            InitiatorDepartmentId = "D1",
        });
        check("流程、应用和发起部门快照筛选不混入其他业务或人员",
            scoped.Flows.Count == 1 && scoped.Flows[0].FlowCode == "PURCHASE" &&
            scoped.People.All(item => item.AssigneeId != "U9"));
        var invalid = false;
        try
        {
            ReportingMetricsCalculator.Calculate(facts, new ReportingMetricsQuery { FromUtc = day, UntilUtc = day });
        }
        catch (ArgumentException)
        {
            invalid = true;
        }

        check("空时间范围被拒绝", invalid);
    }
}
