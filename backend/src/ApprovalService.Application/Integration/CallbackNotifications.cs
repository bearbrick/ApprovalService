using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 在审批事务内把引擎事件固定为可重发的外部通知快照。
/// </summary>
public static class CallbackNotifications
{
    /// <summary>
    /// 将需要通知业务系统的事件映射到稳定的外部事件编码。
    /// </summary>
    /// <param name="item">引擎事件。</param>
    /// <param name="instance">本次提交后的审批单。</param>
    /// <returns>需回调时返回完整快照；内部事件返回空。</returns>
    public static CallbackNotification? Create(EngineEvent item, InstanceEntity instance)
    {
        var eventCode = item.Kind switch
        {
            "instanceStarted" => "instance.started",
            "instanceApproved" => "instance.approved",
            "instanceRejected" => "instance.rejected",
            "instanceWithdrawn" => "instance.withdrawn",
            "instanceTerminated" => "instance.terminated",
            "instanceCancelled" => "instance.cancelled",
            "taskCreated" => "task.created",
            "taskCompleted" => "task.completed",
            "taskCancelled" => "task.cancelled",
            _ => null,
        };
        if (eventCode is null)
        {
            return null;
        }

        // 同一命令可以先发起再立即通过，因此开始事件不能借用最终状态。
        var status = item.Kind == "instanceStarted" ? EngineInstanceStatuses.Running : instance.Status;
        using var data = JsonDocument.Parse(instance.ApprovalDataJson);
        return new CallbackNotification
        {
            EventId = item.Id,
            Event = eventCode,
            InstanceId = instance.Id,
            SerialNo = instance.SerialNo,
            FlowCode = instance.FlowCode,
            BusinessKey = instance.BusinessKey,
            Status = status,
            Data = data.RootElement.Clone(),
            OccurredAt = item.OccurredAt,
        };
    }

    /// <summary>
    /// 使用统一 JSON 规则序列化通知，保证每次重发都读取同一份持久正文。
    /// </summary>
    /// <param name="notification">事件快照。</param>
    /// <returns>稳定的 JSON 请求体。</returns>
    public static string Serialize(CallbackNotification notification) =>
        JsonSerializer.Serialize(notification, ApprovalJson.Options);
}
