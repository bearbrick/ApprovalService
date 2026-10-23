namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 已通过管理员授权的当前本地会话。
/// </summary>
public sealed class AdminSessionResponse
{
    /// <summary>
    /// 管理员对应的内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;
}

/// <summary>
/// 管理后台可读取的组织目录，不含联系方式和账号凭据。
/// </summary>
public sealed class AdminDirectoryResponse
{
    /// <summary>
    /// 部门列表。
    /// </summary>
    public IReadOnlyList<AdminDepartmentResponse> Departments { get; set; } = [];

    /// <summary>
    /// 人员列表。
    /// </summary>
    public IReadOnlyList<AdminPersonResponse> People { get; set; } = [];
}

/// <summary>
/// 管理后台部门摘要。
/// </summary>
public sealed class AdminDepartmentResponse
{
    /// <summary>
    /// 内部部门 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 部门编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 部门名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 上级部门名称；根部门为空。
    /// </summary>
    public string? ParentName
    {
        get; set;
    }

    /// <summary>
    /// 负责人姓名；未设置时为空。
    /// </summary>
    public string? ManagerName
    {
        get; set;
    }

    /// <summary>
    /// 可信主数据来源。
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// 管理后台人员摘要。
/// </summary>
public sealed class AdminPersonResponse
{
    /// <summary>
    /// 内部人员 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 工号。
    /// </summary>
    public string EmployeeNumber { get; set; } = string.Empty;

    /// <summary>
    /// 姓名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 所属部门名称。
    /// </summary>
    public IReadOnlyList<string> Departments { get; set; } = [];

    /// <summary>
    /// 岗位。
    /// </summary>
    public string? Position
    {
        get; set;
    }

    /// <summary>
    /// 在职状态。
    /// </summary>
    public bool IsActive
    {
        get; set;
    }
}

/// <summary>
/// 接入应用的安全摘要，不包含 API Key、签名密钥或地址。
/// </summary>
public sealed class AdminClientAppResponse
{
    /// <summary>
    /// 应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 应用名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 已授权流程编码。
    /// </summary>
    public IReadOnlyList<string> AllowedFlowCodes { get; set; } = [];

    /// <summary>
    /// 是否启用。
    /// </summary>
    public bool IsEnabled
    {
        get; set;
    }
}
