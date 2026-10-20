using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 发给业务系统的审批事件快照；重发时正文和事件 ID 保持不变。
/// </summary>
public sealed class CallbackNotification
{
    /// <summary>
    /// 引擎生成的稳定事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 面向业务系统的事件编码。
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 审批编号。
    /// </summary>
    public string SerialNo { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 事件发生时的审批状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 事件发生时的审批数据快照。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }

    /// <summary>
    /// 本次任务操作中由审批人填写的字段；原业务数据快照仍在 data 中。
    /// </summary>
    public JsonElement? FieldValues
    {
        get; set;
    }

    /// <summary>
    /// 事件发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}
