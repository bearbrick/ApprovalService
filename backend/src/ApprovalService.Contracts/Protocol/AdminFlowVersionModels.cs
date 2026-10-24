namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 管理员可见的不可变发布版本摘要。
/// </summary>
public sealed class AdminFlowVersionResponse
{
    /// <summary>
    /// 不可变版本 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 流程内递增版本号。
    /// </summary>
    public int Version
    {
        get; set;
    }

    /// <summary>
    /// 发布时间。
    /// </summary>
    public DateTimeOffset PublishedAt
    {
        get; set;
    }

    /// <summary>
    /// 是否当前新发起所用发布版。
    /// </summary>
    public bool IsCurrent
    {
        get; set;
    }
}

/// <summary>
/// 将指定旧版内容复制为新草稿修订，之后仍须显式发布。
/// </summary>
public sealed class AdminFlowVersionRestoreRequest
{
    /// <summary>
    /// 操作者上次读取的草稿修订。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 操作者上次读取的当前发布版 ID。
    /// </summary>
    public string? ExpectedPublishedVersionId
    {
        get; set;
    }

    /// <summary>
    /// 复制旧版内容的原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
