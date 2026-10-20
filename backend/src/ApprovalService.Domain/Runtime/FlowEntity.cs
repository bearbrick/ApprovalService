namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 流程的可编辑元数据；发布内容保存在独立的不可变版本中。
/// </summary>
public sealed class FlowEntity
{
    /// <summary>
    /// 流程 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 业务系统使用的稳定编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 流程分组。
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// 流程管理员的工号。
    /// </summary>
    public string? AdministratorId { get; set; }

    /// <summary>
    /// 当前发布版本 ID；尚未发布时为空。
    /// </summary>
    public string? PublishedVersionId { get; set; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// 发布后不再修改的流程版本，包含设计和执行图的完整快照。
/// </summary>
public sealed class FlowVersionEntity
{
    /// <summary>
    /// 版本 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 所属流程 ID。
    /// </summary>
    public string FlowId { get; set; } = string.Empty;

    /// <summary>
    /// 流程内递增的版本号。
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// 设计格式版本号。
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// 自由画布和审批数据定义的 JSON 快照。
    /// </summary>
    public string DefinitionJson { get; set; } = string.Empty;

    /// <summary>
    /// 发布时编译的执行图 JSON。
    /// </summary>
    public string GraphJson { get; set; } = string.Empty;

    /// <summary>
    /// 发布时间。
    /// </summary>
    public DateTimeOffset PublishedAt { get; set; }
}
