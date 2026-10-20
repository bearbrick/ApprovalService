using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 业务系统读取的审批单状态与操作历史。
/// </summary>
public sealed class OpenInstanceResponse
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 用户可见审批编号。
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
    /// 审批标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 发起人内部 ID。
    /// </summary>
    public string InitiatorId { get; set; } = string.Empty;

    /// <summary>
    /// 本次选定的内部发起部门 ID。
    /// </summary>
    public string? InitiatorDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 实际代发人的内部人员 ID；本人发起时为空。
    /// </summary>
    public string? SubmittedById
    {
        get; set;
    }

    /// <summary>
    /// 发起时的来源接入应用。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;

    /// <summary>
    /// 当前审批状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 业务审批数据快照。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }

    /// <summary>
    /// 发起时间。
    /// </summary>
    public DateTimeOffset StartedAt
    {
        get; set;
    }

    /// <summary>
    /// 结束时间；在途时为空。
    /// </summary>
    public DateTimeOffset? EndedAt
    {
        get; set;
    }

    /// <summary>
    /// 人工任务与处理结果。
    /// </summary>
    public IReadOnlyList<OpenInstanceTaskResponse> Tasks { get; set; } = [];

    /// <summary>
    /// 按顺序排列的操作记录。
    /// </summary>
    public IReadOnlyList<OpenOperationResponse> Events { get; set; } = [];
}

/// <summary>
/// 审批单内的一条人工任务。
/// </summary>
public sealed class OpenInstanceTaskResponse
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 内部处理人 ID。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;

    /// <summary>
    /// 任务状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 审批意见。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }

    /// <summary>
    /// 任务到期时间。
    /// </summary>
    public DateTimeOffset? DueAt
    {
        get; set;
    }

    /// <summary>
    /// 实际处理时间。
    /// </summary>
    public DateTimeOffset? CompletedAt
    {
        get; set;
    }
}

/// <summary>
/// 可核对顺序和时间的引擎操作记录。
/// </summary>
public sealed class OpenOperationResponse
{
    /// <summary>
    /// 事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 审批单内顺序号。
    /// </summary>
    public long Sequence
    {
        get; set;
    }

    /// <summary>
    /// 事件类型。
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// 引擎事件与审批意见等审计详情。
    /// </summary>
    public JsonElement Detail
    {
        get; set;
    }

    /// <summary>
    /// 发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}

/// <summary>
/// 单个人的一条待办，含业务系统判断所需的少量摘要字段。
/// </summary>
public sealed class OpenTaskResponse
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

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
    /// 审批标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 当前节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 是否加急。
    /// </summary>
    public bool IsUrgent
    {
        get; set;
    }

    /// <summary>
    /// 到期时间。
    /// </summary>
    public DateTimeOffset? DueAt
    {
        get; set;
    }

    /// <summary>
    /// 流程版本指定的摘要字段和值。
    /// </summary>
    public JsonElement Summary
    {
        get; set;
    }

    /// <summary>
    /// 审批中心内可打开当前审批单的相对地址。
    /// </summary>
    public string OpenPath { get; set; } = string.Empty;
}

/// <summary>
/// 待办数量统计。
/// </summary>
public sealed class OpenTaskCountResponse
{
    /// <summary>
    /// 待处理数量。
    /// </summary>
    public int Pending
    {
        get; set;
    }

    /// <summary>
    /// 其中加急数量。
    /// </summary>
    public int Urgent
    {
        get; set;
    }

    /// <summary>
    /// 其中已超时数量。
    /// </summary>
    public int Overdue
    {
        get; set;
    }
}
