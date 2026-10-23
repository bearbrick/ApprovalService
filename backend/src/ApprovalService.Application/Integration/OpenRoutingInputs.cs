namespace ApprovalService.Application.Integration;

/// <summary>
/// 发起时固定的路由输入；组织人员详情和矩阵命中在后续节点到达时重新读取。
/// </summary>
public sealed class OpenRoutingInputs
{
    /// <summary>
    /// 当前应用绑定的原始发起人员 ID，供后续业务取人钩子使用。
    /// </summary>
    public string? InitiatorExternalId
    {
        get; set;
    }

    /// <summary>
    /// 发起时自选节点到内部人员 ID 的映射。
    /// </summary>
    public Dictionary<string, List<string>> InitiatorSelections { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 审批数据中的人员或部门标识映射到内部主数据 ID。
    /// </summary>
    public Dictionary<string, string> DataValues { get; set; } = new(StringComparer.Ordinal);
}
