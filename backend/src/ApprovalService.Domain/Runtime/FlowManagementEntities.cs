namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 每个流程唯一的可编辑草稿，发布版本只从草稿快照生成。
/// </summary>
public sealed class FlowDraftEntity
{
    /// <summary>
    /// 所属流程 ID。
    /// </summary>
    public string FlowId { get; set; } = string.Empty;

    /// <summary>
    /// 每次编辑单调递增的修订号。
    /// </summary>
    public long Revision
    {
        get; set;
    }

    /// <summary>
    /// 自由画布和审批数据的完整草稿 JSON。
    /// </summary>
    public string DefinitionJson { get; set; } = string.Empty;

    /// <summary>
    /// 最近编辑时间。
    /// </summary>
    public DateTimeOffset UpdatedAt
    {
        get; set;
    }
}

/// <summary>
/// 流程管理操作的最小审计元数据，不保存完整定义。
/// </summary>
public sealed class FlowManagementAuditEntity
{
    /// <summary>
    /// 审计记录 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 所属流程 ID。
    /// </summary>
    public string FlowId { get; set; } = string.Empty;

    /// <summary>
    /// 操作人内部 ID。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// 操作种类。
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 管理员填写的原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 本次关联的草稿修订号。
    /// </summary>
    public long? DraftRevision
    {
        get; set;
    }

    /// <summary>
    /// 本次关联的发布版本号。
    /// </summary>
    public int? PublishedVersion
    {
        get; set;
    }

    /// <summary>
    /// 流程定义内容的 SHA-256；停启用操作为空。
    /// </summary>
    public string? ContentSha256
    {
        get; set;
    }

    /// <summary>
    /// 操作发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}
