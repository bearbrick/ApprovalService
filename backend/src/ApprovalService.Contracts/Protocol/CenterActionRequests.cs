using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 登录人员对当前任务提交一次人工命令，人员参数均为审批服务内部 ID。
/// </summary>
public sealed class CenterTaskActionRequest
{
    /// <summary>
    /// 引擎命令编码。
    /// </summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>
    /// 审批意见或评论。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }
    /// <summary>
    /// 转交、加签、抄送或征询的目标人员 ID。
    /// </summary>
    public string? TargetPersonId
    {
        get; set;
    }
    /// <summary>
    /// 退回的目标节点 ID。
    /// </summary>
    public string? TargetNodeId
    {
        get; set;
    }
    /// <summary>
    /// 征询或加签关联标识。
    /// </summary>
    public string? RelatedId
    {
        get; set;
    }
    /// <summary>
    /// 退回或加签位置等操作模式。
    /// </summary>
    public string? Mode
    {
        get; set;
    }
    /// <summary>
    /// 评论中提及的内部人员 ID。
    /// </summary>
    public List<string> MentionedPersonIds { get; set; } = [];
    /// <summary>
    /// 本节点可写的审批人字段；不覆盖业务数据快照。
    /// </summary>
    public JsonElement? FieldValues
    {
        get; set;
    }
}

/// <summary>
/// 批量同意请求，任务逐项独立提交与返回。
/// </summary>
public sealed class CenterBatchApproveRequest
{
    /// <summary>
    /// 本次要办理的任务 ID，不能重复。
    /// </summary>
    public List<string> TaskIds { get; set; } = [];
    /// <summary>
    /// 各任务共用的意见；必填规则仍由各节点独立校验。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }
}

/// <summary>
/// 单项任务提交后的安全结果。
/// </summary>
public sealed class CenterTaskActionResponse
{
    /// <summary>
    /// 本次办理的任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
    /// <summary>
    /// 操作后可见的审批详情。
    /// </summary>
    public CenterDetailResponse Detail { get; set; } = new();
}

/// <summary>
/// 批量办理中一项任务的独立结果。
/// </summary>
public sealed class CenterBatchApproveItemResponse
{
    /// <summary>
    /// 原请求任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>
    /// 此任务是否成功。
    /// </summary>
    public bool Succeeded
    {
        get; set;
    }
    /// <summary>
    /// 失败时的稳定错误码。
    /// </summary>
    public string? Code
    {
        get; set;
    }
    /// <summary>
    /// 失败时的中文说明。
    /// </summary>
    public string? Message
    {
        get; set;
    }
    /// <summary>
    /// 成功时的审批单 ID。
    /// </summary>
    public string? InstanceId
    {
        get; set;
    }
}

/// <summary>
/// 批量同意的逐项处理结果，已成功项不因其他项失败而回滚。
/// </summary>
public sealed class CenterBatchApproveResponse
{
    /// <summary>
    /// 与请求顺序一致的结果。
    /// </summary>
    public IReadOnlyList<CenterBatchApproveItemResponse> Items { get; set; } = [];
}
