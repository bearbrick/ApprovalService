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

/// <summary>
/// 实际进入业务接口找人节点前发送的请求。
/// </summary>
public sealed class AssigneeHookRequest
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
    /// 本次实际到达的节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 节点配置的业务来源值。
    /// </summary>
    public string? SourceValue
    {
        get; set;
    }

    /// <summary>
    /// 发起人在当前应用已绑定的业务人员 ID。
    /// </summary>
    public string Initiator { get; set; } = string.Empty;

    /// <summary>
    /// 当前审批数据快照。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }
}

/// <summary>
/// 业务系统返回的人员 ID，允许空列表并交给节点缺人规则。
/// </summary>
public sealed class AssigneeHookResponse
{
    /// <summary>
    /// 当前应用已绑定的外部人员 ID 列表。
    /// </summary>
    public List<string>? UserIds
    {
        get; set;
    }
}

/// <summary>
/// 持久待发项在审批事务提交后发送的业务自动动作。
/// </summary>
public sealed class ActionHookRequest
{
    /// <summary>
    /// 触发动作的稳定事件 ID，业务系统据此去重。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 节点配置的动作编码。
    /// </summary>
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 当前审批数据快照。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }
}

/// <summary>
/// 业务自动动作返回的执行结论；失败时待发项继续重试。
/// </summary>
public sealed class ActionHookResponse
{
    /// <summary>
    /// 业务动作是否成功。
    /// </summary>
    public bool? Success
    {
        get; set;
    }

    /// <summary>
    /// 可选的业务提示；不得写入普通日志。
    /// </summary>
    public string? Message
    {
        get; set;
    }

    /// <summary>
    /// 可选的业务结果对象。
    /// </summary>
    public JsonElement? Result
    {
        get; set;
    }
}
