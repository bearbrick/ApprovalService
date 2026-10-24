using ApprovalService.Contracts.Flows;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 管理员创建流程及初始草稿。
/// </summary>
public sealed class AdminFlowCreateRequest
{
    /// <summary>
    /// 完整自由画布草稿。
    /// </summary>
    public FlowDefinition? Definition
    {
        get; set;
    }

    /// <summary>
    /// 流程分组。
    /// </summary>
    public string? Group
    {
        get; set;
    }

    /// <summary>
    /// 直接所属部门内部 ID；空值仅组织管理员可用。
    /// </summary>
    public string? OwnerDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 运行期指定管理员内部人员 ID。
    /// </summary>
    public string? AdministratorId
    {
        get; set;
    }

    /// <summary>
    /// 本次操作原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 管理员按草稿修订保存流程定义和可编辑元数据。
/// </summary>
public sealed class AdminFlowDraftRequest
{
    /// <summary>
    /// 前次读取的草稿修订号。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 替换后的完整草稿。
    /// </summary>
    public FlowDefinition? Definition
    {
        get; set;
    }

    /// <summary>
    /// 流程分组。
    /// </summary>
    public string? Group
    {
        get; set;
    }

    /// <summary>
    /// 直接所属部门内部 ID。
    /// </summary>
    public string? OwnerDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 运行期指定管理员内部人员 ID。
    /// </summary>
    public string? AdministratorId
    {
        get; set;
    }

    /// <summary>
    /// 本次操作原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 管理员发布当前草稿时的双重乐观并发前提。
/// </summary>
public sealed class AdminFlowPublishRequest
{
    /// <summary>
    /// 预期草稿修订号。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 预期当前发布版 ID；首次发布时为空。
    /// </summary>
    public string? ExpectedPublishedVersionId
    {
        get; set;
    }

    /// <summary>
    /// 本次发布原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 管理后台可访问的流程、草稿修订和当前发布版摘要。
/// </summary>
public sealed class AdminFlowResponse
{
    /// <summary>
    /// 稳定流程编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 流程显示名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 流程分组。
    /// </summary>
    public string? Group
    {
        get; set;
    }

    /// <summary>
    /// 直接所属部门内部 ID。
    /// </summary>
    public string? OwnerDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 运行期指定管理员内部人员 ID。
    /// </summary>
    public string? AdministratorId
    {
        get; set;
    }

    /// <summary>
    /// 是否允许新审批单发起。
    /// </summary>
    public bool IsEnabled
    {
        get; set;
    }

    /// <summary>
    /// 当前草稿修订号；历史数据无草稿时为零。
    /// </summary>
    public long DraftRevision
    {
        get; set;
    }

    /// <summary>
    /// 详情读取时包含的完整草稿，列表不返回。
    /// </summary>
    public FlowDefinition? Draft
    {
        get; set;
    }

    /// <summary>
    /// 当前发布版 ID。
    /// </summary>
    public string? PublishedVersionId
    {
        get; set;
    }

    /// <summary>
    /// 当前发布版本号。
    /// </summary>
    public int? PublishedVersion
    {
        get; set;
    }
}
