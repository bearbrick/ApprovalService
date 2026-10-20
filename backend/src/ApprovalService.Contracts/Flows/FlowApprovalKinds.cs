namespace ApprovalService.Contracts.Flows;

/// <summary>
/// 审批人来源编码；岗位与职级分别有编码，设计书把两者列在同一类。
/// </summary>
public static class FlowAssigneeSources
{
    /// <summary>
    /// 指定成员。
    /// </summary>
    public const string Member = "member";
    /// <summary>
    /// 角色。
    /// </summary>
    public const string Role = "role";
    /// <summary>
    /// 岗位。
    /// </summary>
    public const string Position = "position";
    /// <summary>
    /// 职级。
    /// </summary>
    public const string Grade = "grade";
    /// <summary>
    /// 直属上级。
    /// </summary>
    public const string Manager = "manager";
    /// <summary>
    /// 连续多级上级。
    /// </summary>
    public const string ManagerChain = "managerChain";
    /// <summary>
    /// 部门负责人。
    /// </summary>
    public const string DepartmentHead = "departmentHead";
    /// <summary>
    /// 业务数据中的部门负责人。
    /// </summary>
    public const string DataDepartmentHead = "dataDepartmentHead";
    /// <summary>
    /// 分管领导。
    /// </summary>
    public const string DivisionLeader = "divisionLeader";
    /// <summary>
    /// 业务数据中的人员。
    /// </summary>
    public const string DataPerson = "dataPerson";
    /// <summary>
    /// 审批矩阵。
    /// </summary>
    public const string Matrix = "matrix";
    /// <summary>
    /// 业务系统接口。
    /// </summary>
    public const string BusinessApi = "businessApi";
    /// <summary>
    /// 上一步指定。
    /// </summary>
    public const string PreviousSelection = "previousSelection";
    /// <summary>
    /// 发起人自选。
    /// </summary>
    public const string InitiatorSelection = "initiatorSelection";
    /// <summary>
    /// 发起人本人。
    /// </summary>
    public const string Initiator = "initiator";
}

/// <summary>
/// 多人审批方式编码。
/// </summary>
public static class FlowApprovalModes
{
    /// <summary>
    /// 依次审批。
    /// </summary>
    public const string Sequential = "sequential";
    /// <summary>
    /// 会签。
    /// </summary>
    public const string All = "all";
    /// <summary>
    /// 或签。
    /// </summary>
    public const string Any = "any";
    /// <summary>
    /// 票签。
    /// </summary>
    public const string Vote = "vote";
}

/// <summary>
/// 会签遇到拒绝时的处理编码。
/// </summary>
public static class FlowRejectBehaviors
{
    /// <summary>
    /// 立即拒绝。
    /// </summary>
    public const string Immediate = "immediate";
    /// <summary>
    /// 等所有人处理后决定。
    /// </summary>
    public const string WaitAll = "waitAll";
}

/// <summary>
/// 找不到处理人时的处理编码。
/// </summary>
public static class FlowMissingAssigneeBehaviors
{
    /// <summary>
    /// 阻止发起或进入节点。
    /// </summary>
    public const string Block = "block";
    /// <summary>
    /// 自动通过。
    /// </summary>
    public const string Approve = "approve";
    /// <summary>
    /// 转交管理员。
    /// </summary>
    public const string Administrator = "administrator";
    /// <summary>
    /// 转交指定人员。
    /// </summary>
    public const string Specified = "specified";
}

/// <summary>
/// 审批人即发起人时的处理编码。
/// </summary>
public static class FlowSelfApprovalBehaviors
{
    /// <summary>
    /// 跳过本人。
    /// </summary>
    public const string Skip = "skip";
    /// <summary>
    /// 允许本人审批。
    /// </summary>
    public const string Self = "self";
    /// <summary>
    /// 转交直属上级。
    /// </summary>
    public const string Manager = "manager";
}

/// <summary>
/// 同一人重复出现时的处理编码。
/// </summary>
public static class FlowDuplicateAssigneeBehaviors
{
    /// <summary>
    /// 相邻出现只审一次。
    /// </summary>
    public const string AdjacentOnce = "adjacentOnce";
    /// <summary>
    /// 全流程只审一次。
    /// </summary>
    public const string Once = "once";
    /// <summary>
    /// 每次都审。
    /// </summary>
    public const string EveryTime = "everyTime";
}

/// <summary>
/// 审批人离职或停用时的处理编码。
/// </summary>
public static class FlowInactiveAssigneeBehaviors
{
    /// <summary>
    /// 转交直属上级。
    /// </summary>
    public const string Manager = "manager";
    /// <summary>
    /// 转交管理员。
    /// </summary>
    public const string Administrator = "administrator";
}
