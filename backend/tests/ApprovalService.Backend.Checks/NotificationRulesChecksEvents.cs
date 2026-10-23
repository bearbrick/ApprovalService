using System.Text.Json;
using ApprovalService.Application.Notifications;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 验证运行事件的站内及邮件通知在审批事务前可确定且遵守收件人权限。
/// </summary>
internal static class NotificationRuntimeEventChecks
{
    /// <summary>
    /// 执行事件通知规划场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var planner = new RuntimeEventNotificationPlanner();
        var instance = new InstanceEntity
        {
            Id = "instance-1",
            SerialNo = "SP202610000001",
            Title = "采购申请",
            InitiatorId = "initiator",
            SubmittedById = "submitter",
        };
        var graph = new FlowExecutionGraph
        {
            Nodes =
            [
                new FlowExecutionNode
                {
                    Id = "cc-fixed",
                    Assignee = new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "cc, outsider" },
                },
                new FlowExecutionNode
                {
                    Id = "cc-dynamic",
                    Assignee = new FlowAssignee { Source = FlowAssigneeSources.Role, Value = "finance" },
                },
            ],
        };
        var emails = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["approver"] = "approver@example.test",
            ["initiator"] = "initiator@example.test",
            ["submitter"] = "bad address",
            ["cc"] = "cc@example.test",
            ["dynamic-cc"] = "dynamic@example.test",
            ["outsider"] = "outsider@example.test",
        };
        var visible = new HashSet<string>(StringComparer.Ordinal)
        {
            "approver", "initiator", "submitter", "cc", "dynamic-cc",
        };
        var when = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);
        var result = new EngineResult
        {
            State = new EngineState
            {
                Tasks =
                [
                    new EngineTask { Id = "task-1", NodeId = "approve", AssigneeId = "approver", Status = EngineTaskStatuses.Pending },
                    new EngineTask { Id = "task-queued", NodeId = "approve", AssigneeId = "approver", Status = EngineTaskStatuses.Queued },
                ],
            },
            Events =
            [
                Event("event-new", "taskCreated", when, taskId: "task-1"),
                Event("event-remind", "reminderRequested", when, nodeId: "approve"),
                Event("event-copy", "copyRequested", when, audit: new OperationAudit { TargetUserId = "cc" }),
                Event("event-cc", "ccDelivered", when, nodeId: "cc-fixed"),
                new EngineEvent
                {
                    Id = "event-cc-dynamic",
                    Kind = "ccDelivered",
                    OccurredAt = when,
                    NodeId = "cc-dynamic",
                    ResolvedAssigneeIds = ["dynamic-cc", "outsider"],
                },
                Event("event-comment", "commentAdded", when, audit: new OperationAudit
                {
                    MentionedUserIds = ["cc", "outsider"],
                }),
                Event("event-result", "instanceApproved", when),
            ],
        };
        var items = planner.Plan(instance, graph, result, [], emails, visible);
        var messages = items.Select(item => JsonSerializer.Deserialize<NotificationMessage>(item.PayloadJson)!).ToArray();
        check("事件通知仅发送给有权查看审批单的人且排队任务不产生待办提醒",
            messages.All(message => visible.Contains(message.RecipientId)) &&
            !messages.Any(message => message.Body.Contains("task-queued", StringComparison.Ordinal)) &&
            messages.Count(message => message.Subject == "您有新的审批待办") == 2);
        check("事件通知对有效邮箱同时排站内和邮件，无效邮箱仅保留站内",
            messages.Any(message => message.RecipientId == "initiator" && message.ChannelCode == NotificationChannelCodes.Email) &&
            messages.Count(message => message.RecipientId == "submitter") == 1 &&
            messages.Single(message => message.RecipientId == "submitter").ChannelCode == NotificationChannelCodes.InApp);
        check("催办、审批结果、抄送和提及各按原事件排通知",
            messages.Any(message => message.Subject == "审批任务被催办") &&
            messages.Any(message => message.Subject == "审批结果：已通过") &&
            messages.Any(message => message.Subject == "您收到审批抄送") &&
            messages.Any(message => message.Subject == "审批评论提及了您"));
        check("事件通知类别与个人订阅的固定编码一致",
            messages.Where(message => message.Subject == "审批任务被催办").All(message =>
                message.Category == NotificationCategories.Urge) &&
            messages.Where(message => message.Subject == "您收到审批抄送").All(message =>
                message.Category == NotificationCategories.Copy) &&
            messages.Where(message => message.Subject == "审批评论提及了您").All(message =>
                message.Category == NotificationCategories.Mention) &&
            messages.Where(message => message.Subject == "您有新的审批待办").All(message =>
                message.Category == NotificationCategories.NewTask));
        check("固定抄送和动态实际收件人均收到通知，非参与人的提及不发送",
            messages.Count(message => message.RecipientId == "cc" && message.Subject == "您收到审批抄送") == 4 &&
            messages.Count(message => message.RecipientId == "dynamic-cc" && message.Subject == "您收到审批抄送") == 2 &&
            !messages.Any(message => message.RecipientId == "outsider") &&
            items.Count(item => item.EventId == "event-cc-dynamic") == 2);
        check("待发通知具有稳定短 ID 和登录后可访问的站内链接",
            items.All(item => item.Id.Length == 64 && item.Kind == NotificationChannelCodes.Outbox &&
                item.AvailableAt == when) &&
            messages.All(message => message.TargetPath == "/m/approvals/instance-1") &&
            planner.Plan(instance, graph, result, [], emails, visible).Select(item => item.Id).SequenceEqual(
                items.Select(item => item.Id)));

        var previous = new[]
        {
            new ApprovalTaskEntity { Id = "task-1", AssigneeId = "approver", Status = EngineTaskStatuses.Pending },
            new ApprovalTaskEntity { Id = "task-queued", AssigneeId = "approver", Status = EngineTaskStatuses.Queued },
        };
        var activation = new EngineResult
        {
            State = new EngineState
            {
                Tasks =
                [
                    new EngineTask { Id = "task-1", NodeId = "approve", AssigneeId = "approver", Status = EngineTaskStatuses.Pending },
                    new EngineTask { Id = "task-queued", NodeId = "approve", AssigneeId = "approver", Status = EngineTaskStatuses.Pending },
                ],
            },
            Events = [Event("event-activated", "taskActivated", when, taskId: "task-queued")],
        };
        var activated = planner.Plan(instance, graph, activation, previous, emails, visible);
        check("排队任务激活时只给新进入待办的任务通知", activated.Count == 2 &&
            activated.All(item => item.EventId == "event-activated"));
        activation.IsReplay = true;
        check("幂等重放不重复生成通知", planner.Plan(instance, graph, activation, previous, emails, visible).Count == 0);
    }

    /// <summary>
    /// 建立一条时间固定的引擎事件。
    /// </summary>
    /// <param name="id">事件 ID。</param>
    /// <param name="kind">事件种类。</param>
    /// <param name="when">固定发生时间。</param>
    /// <param name="nodeId">可选节点 ID。</param>
    /// <param name="taskId">可选任务 ID。</param>
    /// <param name="audit">可选操作审计。</param>
    /// <returns>用于测试的事件。</returns>
    private static EngineEvent Event(string id, string kind, DateTimeOffset when, string? nodeId = null,
        string? taskId = null, OperationAudit? audit = null) => new()
        {
            Id = id,
            Kind = kind,
            OccurredAt = when,
            NodeId = nodeId,
            TaskId = taskId,
            Audit = audit,
        };
}
