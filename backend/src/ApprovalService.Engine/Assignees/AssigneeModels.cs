using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 供审批人解析使用的人员快照。
/// </summary>
public sealed class AssigneePerson
{
    /// <summary>
    /// 人员 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 人员所属部门 ID。
    /// </summary>
    public List<string> DepartmentIds { get; set; } = [];
    /// <summary>
    /// 直属上级 ID。
    /// </summary>
    public string? ManagerId
    {
        get; set;
    }
    /// <summary>
    /// 角色编码。
    /// </summary>
    public List<string> Roles { get; set; } = [];
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
    /// 职级序数，数字越大级别越高。
    /// </summary>
    public int GradeRank
    {
        get; set;
    }
    /// <summary>
    /// 是否在职且账号启用。
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 供审批人解析使用的部门快照。
/// </summary>
public sealed class AssigneeDepartment
{
    /// <summary>
    /// 部门 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 上级部门 ID。
    /// </summary>
    public string? ParentId
    {
        get; set;
    }
    /// <summary>
    /// 部门负责人 ID。
    /// </summary>
    public string? HeadId
    {
        get; set;
    }
    /// <summary>
    /// 分管领导 ID，允许多人。
    /// </summary>
    public List<string> DivisionLeaderIds { get; set; } = [];
    /// <summary>
    /// 按分管业务编码划分的领导人员。
    /// </summary>
    public Dictionary<string, List<string>> DivisionLeadersByArea { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 管理后台维护的审批矩阵命中项。匹配条件已由调用方对业务快照求值。
/// </summary>
public sealed class AssigneeMatrixMatch
{
    /// <summary>
    /// 矩阵编码。
    /// </summary>
    public string MatrixCode { get; set; } = string.Empty;
    /// <summary>
    /// 命中的人员 ID。
    /// </summary>
    public List<string> AssigneeIds { get; set; } = [];
}

/// <summary>
/// 一次节点到达时的纯输入快照。业务接口结果由出站适配层提前取得。
/// </summary>
public sealed class AssigneeSnapshot
{
    /// <summary>
    /// 发起人 ID。
    /// </summary>
    public string InitiatorId { get; set; } = string.Empty;
    /// <summary>
    /// 本次发起部门 ID；缺省时由调用方填入主部门。
    /// </summary>
    public string InitiatingDepartmentId { get; set; } = string.Empty;
    /// <summary>
    /// 人员快照。
    /// </summary>
    public List<AssigneePerson> People { get; set; } = [];
    /// <summary>
    /// 部门快照。
    /// </summary>
    public List<AssigneeDepartment> Departments { get; set; } = [];
    /// <summary>
    /// 审批数据字段的字符串化人员或部门 ID。
    /// </summary>
    public Dictionary<string, string> DataValues { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 审批矩阵本次命中项。
    /// </summary>
    public List<AssigneeMatrixMatch> MatrixMatches { get; set; } = [];
    /// <summary>
    /// 业务系统接口的预取人员，键为接口编码。
    /// </summary>
    public Dictionary<string, List<string>> BusinessApiResults { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 上一步审批人指定的下一步人员，键为节点 ID。
    /// </summary>
    public Dictionary<string, List<string>> PreviousSelections { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 发起时自选的人员，键为节点 ID。
    /// </summary>
    public Dictionary<string, List<string>> InitiatorSelections { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 流程管理员 ID。
    /// </summary>
    public string? AdministratorId
    {
        get; set;
    }
}

/// <summary>
/// 一次节点到达的找人请求。
/// </summary>
public sealed class AssigneeRequest
{
    /// <summary>
    /// 人工节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;
    /// <summary>
    /// 流程定义里的来源配置。
    /// </summary>
    public FlowAssignee Configuration { get; set; } = new();
    /// <summary>
    /// 角色是否限制为发起部门。
    /// </summary>
    public bool LimitToInitiatingDepartment
    {
        get; set;
    }
    /// <summary>
    /// 连续上级遇到此职级即停止，包含该上级。
    /// </summary>
    public string? StopAtGrade
    {
        get; set;
    }
}

/// <summary>
/// 找人结果。原顺序不变，同一人员在单节点只保留一次。
/// </summary>
public sealed class AssigneeResolution
{
    /// <summary>
    /// 人员 ID，按审批顺序排列。
    /// </summary>
    public List<string> AssigneeIds { get; set; } = [];
}
