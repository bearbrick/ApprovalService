using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 只传单号发起时向业务系统请求审批数据。
/// </summary>
public sealed class DataHookRequest
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 本次请求 ID。
    /// </summary>
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// 业务系统提供的审批数据及可选展示信息。
/// </summary>
public sealed class DataHookResponse
{
    /// <summary>
    /// 业务数据对象，必须存在。
    /// </summary>
    public JsonElement? Data
    {
        get; set;
    }

    /// <summary>
    /// 业务单据标题。
    /// </summary>
    public string? Title
    {
        get; set;
    }

    /// <summary>
    /// 业务原单详情地址。
    /// </summary>
    public string? DetailUrl
    {
        get; set;
    }
}

/// <summary>
/// 审批发起或节点通过前的业务校验请求。
/// </summary>
public sealed class ValidationHookRequest
{
    /// <summary>
    /// start 或 approve。
    /// </summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 已发起审批单的 ID；发起时为空。
    /// </summary>
    public string? InstanceId
    {
        get; set;
    }

    /// <summary>
    /// 被审批任务 ID；发起时为空。
    /// </summary>
    public string? TaskId
    {
        get; set;
    }

    /// <summary>
    /// 当前应用已绑定的业务人员 ID。
    /// </summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>
    /// 当前审批数据快照。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }
}

/// <summary>
/// 业务系统校验结果；拒绝时必须提供原因。
/// </summary>
public sealed class ValidationHookResponse
{
    /// <summary>
    /// 是否允许继续审批。
    /// </summary>
    public bool? Allowed
    {
        get; set;
    }

    /// <summary>
    /// 拒绝的业务原因。
    /// </summary>
    public string? Reason
    {
        get; set;
    }
}
