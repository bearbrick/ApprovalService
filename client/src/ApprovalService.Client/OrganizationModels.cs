namespace ApprovalService.Client;

/// <summary>
/// 业务系统推送的组织变化批次。
/// </summary>
public sealed class OrganizationChangesRequest
{
    /// <summary>
    /// 部门资料。
    /// </summary>
    public List<OrganizationDepartment> Departments { get; set; } = new();

    /// <summary>
    /// 人员资料。
    /// </summary>
    public List<OrganizationPerson> People { get; set; } = new();

    /// <summary>
    /// 人员部门关系。
    /// </summary>
    public List<OrganizationMembership> Memberships { get; set; } = new();
}

/// <summary>
/// 一个部门的主数据。
/// </summary>
public sealed class OrganizationDepartment
{
    /// <summary>
    /// 审批服务内部部门 ID。
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
    /// 上级部门 ID。
    /// </summary>
    public string? ParentId
    {
        get; set;
    }

    /// <summary>
    /// 负责人内部人员 ID。
    /// </summary>
    public string? HeadPersonId
    {
        get; set;
    }
}

/// <summary>
/// 一个人员的主数据。
/// </summary>
public sealed class OrganizationPerson
{
    /// <summary>
    /// 审批服务内部人员 ID。
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
    /// 手机号。
    /// </summary>
    public string? Phone
    {
        get; set;
    }

    /// <summary>
    /// 邮箱。
    /// </summary>
    public string? Email
    {
        get; set;
    }

    /// <summary>
    /// 直属上级内部人员 ID。
    /// </summary>
    public string? ManagerId
    {
        get; set;
    }

    /// <summary>
    /// 岗位编码。
    /// </summary>
    public string? Position
    {
        get; set;
    }

    /// <summary>
    /// 职级编码。
    /// </summary>
    public string? Grade
    {
        get; set;
    }

    /// <summary>
    /// 职级序数。
    /// </summary>
    public int GradeRank
    {
        get; set;
    }

    /// <summary>
    /// 是否在职。
    /// </summary>
    public bool IsActive
    {
        get; set;
    }
}

/// <summary>
/// 人员所属部门关系。
/// </summary>
public sealed class OrganizationMembership
{
    /// <summary>
    /// 内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// 内部部门 ID。
    /// </summary>
    public string DepartmentId { get; set; } = string.Empty;

    /// <summary>
    /// 是否主部门。
    /// </summary>
    public bool IsPrimary
    {
        get; set;
    }
}

/// <summary>
/// 接入应用建立外部人员或部门标识绑定。
/// </summary>
public sealed class OrganizationBinding
{
    /// <summary>
    /// person 或 department。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 本应用的人员或部门 ID。
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// 部门绑定所需的内部 ID；人员绑定不允许直接指定。
    /// </summary>
    public string? InternalId
    {
        get; set;
    }

    /// <summary>
    /// 人员自动匹配工号。
    /// </summary>
    public string? EmployeeNumber
    {
        get; set;
    }

    /// <summary>
    /// 人员自动匹配手机号。
    /// </summary>
    public string? Phone
    {
        get; set;
    }
}

/// <summary>
/// 组织推送结果。
/// </summary>
public sealed class OrganizationChangesResult
{
    /// <summary>
    /// 是否完成写入。
    /// </summary>
    public bool Applied
    {
        get; set;
    }

    /// <summary>
    /// 字段校验问题。
    /// </summary>
    public IReadOnlyList<OrganizationIssue> Issues { get; set; } = Array.Empty<OrganizationIssue>();

    /// <summary>
    /// 待管理员交接的离职人员 ID。
    /// </summary>
    public IReadOnlyList<string> DepartedPersonIds { get; set; } = Array.Empty<string>();
}

/// <summary>
/// 组织批次中的字段校验问题。
/// </summary>
public sealed class OrganizationIssue
{
    /// <summary>
    /// 字段路径。
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// 统一错误码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 中文问题说明。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
