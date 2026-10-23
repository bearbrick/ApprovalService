namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 历史参与人的两种可信来源。
/// </summary>
public static class InstanceParticipantKinds
{
    /// <summary>
    /// 已送达的普通或人工抄送。
    /// </summary>
    public const string CopyRecipient = "copyRecipient";

    /// <summary>
    /// 实际完成过人工任务的操作人。
    /// </summary>
    public const string TaskActor = "taskActor";
}

/// <summary>
/// 审批命令提交时固定的实际任务处理人或已送达抄送收件人。
/// </summary>
public sealed class InstanceParticipantEntity
{
    /// <summary>
    /// 所属审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 可信内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// 参与来源：抄送收件人或实际任务处理人。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 首次授予可见性的操作事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 首次获得可见性的时间。
    /// </summary>
    public DateTimeOffset GrantedAt
    {
        get; set;
    }
}
