using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 从一次引擎命令的最终状态和事件生成与审批事务同存的通知待发项。
/// </summary>
public sealed class RuntimeEventNotificationPlanner
{
    /// <summary>
    /// 规划新待办、催办、审批结果、抄送及评论提及的站内和邮件通知。
    /// </summary>
    /// <param name="instance">本次提交后的审批单。</param>
    /// <param name="graph">审批单锁定的发布执行图。</param>
    /// <param name="result">引擎本次命令结果。</param>
    /// <param name="previousTasks">命令前已持久化的任务投影。</param>
    /// <param name="recipientEmails">当前有效人员 ID 到邮箱的映射；非在职人员不得提供邮箱。</param>
    /// <param name="authorizedRecipientIds">本次命令完成后有权查看审批单的人员 ID；保密流程必须只传参与人。</param>
    /// <returns>应与审批状态、任务和事件一起提交的通知待发项。</returns>
    public IReadOnlyList<OutboxItemEntity> Plan(InstanceEntity instance, FlowExecutionGraph graph,
        EngineResult result, IReadOnlyList<ApprovalTaskEntity> previousTasks,
        IReadOnlyDictionary<string, string?> recipientEmails, IReadOnlySet<string> authorizedRecipientIds)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(previousTasks);
        ArgumentNullException.ThrowIfNull(recipientEmails);
        ArgumentNullException.ThrowIfNull(authorizedRecipientIds);
        if (result.IsReplay || result.Events.Count == 0)
        {
            return [];
        }

        var previous = previousTasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var messages = new List<OutboxItemEntity>();
        foreach (var task in result.State.Tasks.Where(task => task.Status == EngineTaskStatuses.Pending))
        {
            if (previous.TryGetValue(task.Id, out var old) && old.Status == EngineTaskStatuses.Pending &&
                old.AssigneeId == task.AssigneeId)
            {
                continue;
            }

            // 一个命令可能同时创建和激活任务；只用最后一条关联事件固定一次新待办提醒。
            var source = result.Events.LastOrDefault(item => item.TaskId == task.Id) ?? result.Events[^1];
            Add(messages, instance, source, task.AssigneeId, NotificationCategories.NewTask, "您有新的审批待办",
                "有一条新的审批任务待处理。", recipientEmails, authorizedRecipientIds, task.Id);
            if (!string.IsNullOrWhiteSpace(task.DelegatorId))
            {
                Add(messages, instance, source, task.DelegatorId, NotificationCategories.NewTask,
                    "您委托的审批有新待办", "您委托的审批任务已进入待办，您仍可处理。",
                    recipientEmails, authorizedRecipientIds, task.Id);
            }
        }

        foreach (var source in result.Events)
        {
            switch (source.Kind)
            {
                case "reminderRequested":
                    foreach (var task in result.State.Tasks.Where(task => task.NodeId == source.NodeId &&
                        task.Status == EngineTaskStatuses.Pending))
                    {
                        Add(messages, instance, source, task.AssigneeId, NotificationCategories.Urge, "审批任务被催办",
                            "发起人催办了您的待处理审批任务。", recipientEmails, authorizedRecipientIds);
                        if (!string.IsNullOrWhiteSpace(task.DelegatorId))
                        {
                            Add(messages, instance, source, task.DelegatorId, NotificationCategories.Urge,
                                "委托任务被催办",
                                "发起人催办了您委托的审批任务。", recipientEmails, authorizedRecipientIds);
                        }
                    }

                    break;
                case "instanceApproved":
                case "instanceRejected":
                case "instanceWithdrawn":
                case "instanceTerminated":
                case "instanceCancelled":
                    var outcome = source.Kind switch
                    {
                        "instanceApproved" => "已通过",
                        "instanceRejected" => "已拒绝",
                        "instanceWithdrawn" => "已撤回",
                        "instanceTerminated" => "已终止",
                        _ => "已撤销",
                    };
                    Add(messages, instance, source, instance.InitiatorId, NotificationCategories.Result,
                        "审批结果：" + outcome,
                        "您发起的审批" + outcome + "。", recipientEmails, authorizedRecipientIds);
                    if (!string.IsNullOrWhiteSpace(instance.SubmittedById))
                    {
                        Add(messages, instance, source, instance.SubmittedById, NotificationCategories.Result,
                            "代发审批结果：" + outcome,
                            "您代发的审批" + outcome + "。", recipientEmails, authorizedRecipientIds);
                    }

                    break;
                case "copyRequested":
                    Add(messages, instance, source, source.Audit?.TargetUserId, NotificationCategories.Copy,
                        "您收到审批抄送",
                        "一条审批已抄送给您。", recipientEmails, authorizedRecipientIds);
                    break;
                case "ccDelivered":
                    // 新事件已固定实际收件人；旧事件缺少该字段时才从静态发布配置恢复。
                    var copyRecipients = source.ResolvedAssigneeIds.Count > 0 ? source.ResolvedAssigneeIds :
                        GetFixedCopyRecipients(graph, source.NodeId, instance.InitiatorId);
                    foreach (var recipientId in copyRecipients)
                    {
                        Add(messages, instance, source, recipientId, NotificationCategories.Copy, "您收到审批抄送",
                            "一条审批已抄送给您。", recipientEmails, authorizedRecipientIds);
                    }

                    break;
                case "commentAdded":
                    foreach (var recipientId in source.Audit?.MentionedUserIds ?? [])
                    {
                        Add(messages, instance, source, recipientId, NotificationCategories.Mention,
                            "审批评论提及了您",
                            "有人在审批评论中提及了您。", recipientEmails, authorizedRecipientIds);
                    }

                    break;
            }
        }

        return messages;
    }

    /// <summary>
    /// 仅从发布图中无需访问组织或业务系统即可确定的来源解析抄送收件人。
    /// </summary>
    /// <param name="graph">锁定的发布执行图。</param>
    /// <param name="nodeId">实际送达的抄送节点 ID。</param>
    /// <param name="initiatorId">审批发起人 ID。</param>
    /// <returns>确定的收件人 ID；动态来源留给后续可靠的收件人投影。</returns>
    private static IEnumerable<string> GetFixedCopyRecipients(FlowExecutionGraph graph, string? nodeId,
        string initiatorId)
    {
        var node = graph.Nodes.SingleOrDefault(item => item.Id == nodeId);
        if (node?.Assignee?.Source == FlowAssigneeSources.Initiator)
        {
            return [initiatorId];
        }

        if (node?.Assignee?.Source == FlowAssigneeSources.Member)
        {
            return (node.Assignee.Value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal);
        }

        return [];
    }

    /// <summary>
    /// 仅为具有查看权限的有效收件人写入站内消息，邮箱有效时另写邮件。
    /// </summary>
    /// <param name="items">同一事务的待发项集合。</param>
    /// <param name="instance">本次提交后的审批单。</param>
    /// <param name="source">触发本次通知的引擎事件。</param>
    /// <param name="recipientId">目标人员 ID。</param>
    /// <param name="category">消息种类。</param>
    /// <param name="subject">通知标题。</param>
    /// <param name="action">通知正文中的动作。</param>
    /// <param name="recipientEmails">有效人员邮箱映射。</param>
    /// <param name="authorizedRecipientIds">具备审批单查看权限的人员。</param>
    /// <param name="taskId">同一事件产生多项待办时用于区分通知的任务 ID。</param>
    private static void Add(List<OutboxItemEntity> items, InstanceEntity instance, EngineEvent source,
        string? recipientId, string category, string subject, string action,
        IReadOnlyDictionary<string, string?> recipientEmails, IReadOnlySet<string> authorizedRecipientIds,
        string? taskId = null)
    {
        if (string.IsNullOrWhiteSpace(recipientId) || !authorizedRecipientIds.Contains(recipientId))
        {
            return;
        }

        var body = $"审批单 {instance.SerialNo}（{instance.Title}）：{action}";
        var path = "/m/approvals/" + Uri.EscapeDataString(instance.Id);
        AddChannel(items, instance.Id, source, recipientId, category, taskId, NotificationChannelCodes.InApp,
            null, subject, body, path);
        if (recipientEmails.TryGetValue(recipientId, out var email) &&
            !string.IsNullOrWhiteSpace(email) && MailAddress.TryCreate(email, out var address) &&
            address.Address == email)
        {
            AddChannel(items, instance.Id, source, recipientId, category, taskId, NotificationChannelCodes.Email,
                email, subject, body, path);
        }
    }

    /// <summary>
    /// 从事件、种类、收件人和渠道派生稳定主键，并保持通知正文固定。
    /// </summary>
    /// <param name="items">同一事务的待发项集合。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="source">来源事件。</param>
    /// <param name="recipientId">内部收件人。</param>
    /// <param name="category">消息种类。</param>
    /// <param name="taskId">同一来源事件下的任务区分值。</param>
    /// <param name="channel">发送渠道。</param>
    /// <param name="address">邮件地址。</param>
    /// <param name="subject">标题。</param>
    /// <param name="body">正文。</param>
    /// <param name="path">需登录的站内地址。</param>
    private static void AddChannel(List<OutboxItemEntity> items, string instanceId, EngineEvent source,
        string recipientId, string category, string? taskId, string channel, string? address, string subject,
        string body, string path)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            source.Id + "\n" + category + "\n" + taskId + "\n" + recipientId + "\n" + channel))).ToLowerInvariant();
        if (items.Any(item => item.Id == id))
        {
            return;
        }

        var eventId = source.Id.Length <= 64 ? source.Id :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Id))).ToLowerInvariant();
        var message = new NotificationMessage
        {
            Id = id,
            InstanceId = instanceId,
            EventId = eventId,
            ChannelCode = channel,
            Category = category,
            RecipientId = recipientId,
            RecipientAddress = address,
            Subject = subject,
            Body = body,
            TargetPath = path,
            CreatedAt = source.OccurredAt,
        };
        items.Add(new OutboxItemEntity
        {
            Id = id,
            InstanceId = instanceId,
            EventId = eventId,
            Kind = NotificationChannelCodes.Outbox,
            PayloadJson = JsonSerializer.Serialize(message),
            AvailableAt = source.OccurredAt,
        });
    }
}
