namespace ApprovalService.Client;

/// <summary>
/// 接入应用可使用的已发布流程。
/// </summary>
public sealed class AvailableFlow
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 流程名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 当前发布版本号。
    /// </summary>
    public int Version
    {
        get; set;
    }
}

/// <summary>
/// 流程发起所需的数据定义和 JSON 样例。
/// </summary>
public sealed class FlowSchema
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 当前发布版本号。
    /// </summary>
    public int Version
    {
        get; set;
    }

    /// <summary>
    /// 审批数据字段与摘要字段定义，保持服务返回的 JSON 结构。
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// 按当前字段定义生成的 JSON 样例。
    /// </summary>
    public Dictionary<string, object?> Example { get; set; } = new();
}

/// <summary>
/// 审批单状态、业务数据、任务和操作记录。
/// </summary>
public sealed class ApprovalInstance
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
    /// 发起部门内部 ID。
    /// </summary>
    public string? InitiatorDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 代发人内部 ID。
    /// </summary>
    public string? SubmittedById
    {
        get; set;
    }

    /// <summary>
    /// 来源接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;

    /// <summary>
    /// 当前审批状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 业务审批数据快照。
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// 已成功推进的审批人填写字段当前值，不覆盖原业务数据。
    /// </summary>
    public Dictionary<string, object?> ApproverFieldValues { get; set; } = new();

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
    /// 人工任务和处理结果。
    /// </summary>
    public IReadOnlyList<ApprovalInstanceTask> Tasks { get; set; } = Array.Empty<ApprovalInstanceTask>();

    /// <summary>
    /// 按顺序排列的操作记录。
    /// </summary>
    public IReadOnlyList<ApprovalOperation> Events { get; set; } = Array.Empty<ApprovalOperation>();
}

/// <summary>
/// 审批单中的一条人工任务。
/// </summary>
public sealed class ApprovalInstanceTask
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
    /// 到期时间。
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
/// 审批单的一条引擎操作记录。
/// </summary>
public sealed class ApprovalOperation
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
    /// 引擎事件和意见等审计详情。
    /// </summary>
    public Dictionary<string, object?> Detail { get; set; } = new();

    /// <summary>
    /// 发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}

/// <summary>
/// 一个人的待办数量。
/// </summary>
public sealed class ApprovalTaskCount
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

/// <summary>
/// 一条待办及业务系统显示所需的摘要。
/// </summary>
public sealed class ApprovalTask
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
    public Dictionary<string, object?> Summary { get; set; } = new();

    /// <summary>
    /// 审批中心可打开本单的相对路径。
    /// </summary>
    public string OpenPath { get; set; } = string.Empty;
}
