namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 当前参与人安全可见的已到达审批路径。
/// </summary>
public sealed class CenterProgressResponse
{
    /// <summary>
    /// 已到达及当前节点。
    /// </summary>
    public IReadOnlyList<CenterProgressNodeResponse> Nodes { get; set; } = [];

    /// <summary>
    /// 两端均已到达的连线；不携带分支表达式。
    /// </summary>
    public IReadOnlyList<CenterProgressEdgeResponse> Edges { get; set; } = [];
}

/// <summary>
/// 审批路径中一个已到达节点的显示信息。
/// </summary>
public sealed class CenterProgressNodeResponse
{
    /// <summary>
    /// 锁定版本的稳定节点 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// BPMN 节点种类。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 已到达节点的发布名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 设计画布横坐标。
    /// </summary>
    public double X
    {
        get; set;
    }

    /// <summary>
    /// 设计画布纵坐标。
    /// </summary>
    public double Y
    {
        get; set;
    }

    /// <summary>
    /// 当前运行令牌是否位于此节点。
    /// </summary>
    public bool IsActive
    {
        get; set;
    }
}

/// <summary>
/// 已到达节点之间的安全连线。
/// </summary>
public sealed class CenterProgressEdgeResponse
{
    /// <summary>
    /// 稳定连线 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 来源节点 ID。
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// 目标节点 ID。
    /// </summary>
    public string TargetId { get; set; } = string.Empty;
}

/// <summary>
/// 任务操作中仅对当前人员可见的一个可选关联目标。
/// </summary>
public sealed class CenterRelatedTargetResponse
{
    /// <summary>
    /// 适用操作编码。
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 引擎可核验的关联目标 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 可安全展示的目标说明。
    /// </summary>
    public string Label { get; set; } = string.Empty;
}
