namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 管理员导出时可选的内容来源。
/// </summary>
public static class AdminFlowTransferSources
{
    /// <summary>
    /// 当前可编辑草稿。
    /// </summary>
    public const string Draft = "draft";

    /// <summary>
    /// 指定不可变发布版本。
    /// </summary>
    public const string Published = "published";
}

/// <summary>
/// 只预检本服务兼容 BPMN 文件，不写入草稿。
/// </summary>
public sealed class AdminFlowImportPreviewRequest
{
    /// <summary>
    /// 本服务导出的 BPMN 2.0 XML。
    /// </summary>
    public string BpmnXml { get; set; } = string.Empty;
}

/// <summary>
/// 将兼容 BPMN 内容创建或覆盖为一份草稿。
/// </summary>
public sealed class AdminFlowImportRequest
{
    /// <summary>
    /// 本服务导出的 BPMN 2.0 XML。
    /// </summary>
    public string BpmnXml { get; set; } = string.Empty;

    /// <summary>
    /// 显式覆盖目标编码；省略时只能新建且同编码返回冲突。
    /// </summary>
    public string? TargetCode
    {
        get; set;
    }

    /// <summary>
    /// 覆盖目标时必填的预期草稿修订。
    /// </summary>
    public long? ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 新建流程的分组；覆盖时空值保留已有分组。
    /// </summary>
    public string? Group
    {
        get; set;
    }

    /// <summary>
    /// 新建流程的直接所属部门；覆盖时空值保留已有归属。
    /// </summary>
    public string? OwnerDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 新建流程的运行期管理员；覆盖时空值保留已有人员。
    /// </summary>
    public string? AdministratorId
    {
        get; set;
    }

    /// <summary>
    /// 变更原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 导入预检的兼容性、冲突及发布检查结果。
/// </summary>
public sealed class AdminFlowImportPreviewResponse
{
    /// <summary>
    /// 文件中的流程编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 文件中的显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 画布节点数。
    /// </summary>
    public int NodeCount
    {
        get; set;
    }

    /// <summary>
    /// 是否存在同编码流程。
    /// </summary>
    public bool HasCodeConflict
    {
        get; set;
    }

    /// <summary>
    /// 当前人员是否可显式覆盖同编码流程。
    /// </summary>
    public bool CanOverwrite
    {
        get; set;
    }

    /// <summary>
    /// 可覆盖时的当前草稿修订号。
    /// </summary>
    public long? ExistingDraftRevision
    {
        get; set;
    }

    /// <summary>
    /// 导入后若立即发布仍需解决的问题；不影响保存草稿。
    /// </summary>
    public IReadOnlyList<AdminFlowImportIssue> PublishIssues { get; set; } = [];
}

/// <summary>
/// BPMN 导入预检中可定位的一条发布检查问题。
/// </summary>
public sealed class AdminFlowImportIssue
{
    /// <summary>
    /// 出问题的节点、连线或字段 ID。
    /// </summary>
    public string? ElementId
    {
        get; set;
    }

    /// <summary>
    /// 中文问题说明。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
