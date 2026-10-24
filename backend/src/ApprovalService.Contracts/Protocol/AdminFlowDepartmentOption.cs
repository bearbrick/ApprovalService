namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 当前管理员可作为流程直接所属部门的安全选项。
/// </summary>
public sealed class AdminFlowDepartmentOption
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
