namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 审批单的持久化状态；业务单据本身仍保存在业务系统。
/// </summary>
public sealed class InstanceEntity
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 用户可见的审批编号。
    /// </summary>
    public string SerialNo { get; set; } = string.Empty;

    /// <summary>
    /// 流程 ID。
    /// </summary>
    public string FlowId { get; set; } = string.Empty;

    /// <summary>
    /// 发起时固定的流程版本 ID。
    /// </summary>
    public string FlowVersionId { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码快照。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务系统自己的单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 审批标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 发起人的工号。
    /// </summary>
    public string InitiatorId { get; set; } = string.Empty;

    /// <summary>
    /// 发起部门编码。
    /// </summary>
    public string? InitiatorDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 代发人的工号。
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
    /// 业务系统传来的审批数据快照。
    /// </summary>
    public string ApprovalDataJson { get; set; } = "{}";

    /// <summary>
    /// 业务详情地址。
    /// </summary>
    public string? DetailUrl
    {
        get; set;
    }

    /// <summary>
    /// 是否加急。
    /// </summary>
    public bool IsUrgent
    {
        get; set;
    }

    /// <summary>
    /// 审批单状态编码。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 引擎令牌和任务的完整状态 JSON。
    /// </summary>
    public string StateJson { get; set; } = string.Empty;

    /// <summary>
    /// 乐观锁版本，每次命令提交加一。
    /// </summary>
    public long Version
    {
        get; set;
    }

    /// <summary>
    /// 所属父审批单 ID。
    /// </summary>
    public string? ParentInstanceId
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
    /// 结束时间；未结束时为空。
    /// </summary>
    public DateTimeOffset? EndedAt
    {
        get; set;
    }
}

/// <summary>
/// 便于查询待办的任务投影，引擎状态仍是执行时的权威数据。
/// </summary>
public sealed class ApprovalTaskEntity
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
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 处理人工号。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;

    /// <summary>
    /// 代理时原审批人工号。
    /// </summary>
    public string? DelegatedForId
    {
        get; set;
    }

    /// <summary>
    /// 任务状态编码。
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
    /// 签名文件 ID。
    /// </summary>
    public string? SignatureFileId
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
    /// 处理时间。
    /// </summary>
    public DateTimeOffset? CompletedAt
    {
        get; set;
    }
}
