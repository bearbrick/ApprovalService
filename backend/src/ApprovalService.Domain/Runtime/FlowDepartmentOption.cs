namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 流程管理范围内的部门内部标识与显示名。
/// </summary>
public sealed class FlowDepartmentOption
{
    /// <summary>
    /// 部门内部 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 部门名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
