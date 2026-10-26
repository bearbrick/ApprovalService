namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 角色安全摘要与当前修订。
/// </summary>
public sealed class ManagedRoleResponse
{
    /// <summary>
    /// 不可变角色编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 名称和成员共同使用的修订。
    /// </summary>
    public long Revision
    {
        get; set;
    }
    /// <summary>
    /// 是否为系统内置管理角色。
    /// </summary>
    public bool IsBuiltIn
    {
        get; set;
    }
    /// <summary>
    /// 当前成员的内部人员 ID。
    /// </summary>
    public IReadOnlyList<string> PersonIds { get; set; } = [];
}

/// <summary>
/// 创建自定义角色的请求。
/// </summary>
public sealed class CreateManagedRoleRequest
{
    /// <summary>
    /// 不可变角色编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 审计原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 修改角色名称的请求，不接受编码变更。
/// </summary>
public sealed class UpdateManagedRoleRequest
{
    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 页面读取的预期修订。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }
    /// <summary>
    /// 审计原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 完整替换角色成员的请求。
/// </summary>
public sealed class UpdateRoleMembersRequest
{
    /// <summary>
    /// 目标内部人员 ID 列表。
    /// </summary>
    public IReadOnlyList<string> PersonIds { get; set; } = [];
    /// <summary>
    /// 页面读取的预期修订。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }
    /// <summary>
    /// 审计原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
