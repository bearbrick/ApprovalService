namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 在途审批单的业务唯一键；结束后删除，历史审批单仍可保留相同业务单号。
/// </summary>
public sealed class ActiveInstanceKeyEntity
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务系统单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 当前在途审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
}
