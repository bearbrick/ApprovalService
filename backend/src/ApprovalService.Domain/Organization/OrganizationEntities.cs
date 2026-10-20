namespace ApprovalService.Domain.Organization;

/// <summary>
/// 审批服务维护的部门。
/// </summary>
public sealed class DepartmentEntity
{
    /// <summary>
    /// 部门 ID。
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
    /// 负责人 ID。
    /// </summary>
    public string? HeadPersonId
    {
        get; set;
    }
    /// <summary>
    /// 分管领导 ID 列表的 JSON。
    /// </summary>
    public string DivisionLeaderIdsJson { get; set; } = "[]";
    /// <summary>
    /// 主数据来源编码。
    /// </summary>
    public string TrustedSource { get; set; } = "approvalService";
    /// <summary>
    /// 乐观锁版本。
    /// </summary>
    public long Version
    {
        get; set;
    }
}

/// <summary>
/// 审批服务维护的人员。
/// </summary>
public sealed class PersonEntity
{
    /// <summary>
    /// 人员 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 唯一工号。
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
    /// 直属上级人员 ID。
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
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// 主数据来源编码。
    /// </summary>
    public string TrustedSource { get; set; } = "approvalService";
    /// <summary>
    /// 乐观锁版本。
    /// </summary>
    public long Version
    {
        get; set;
    }
}

/// <summary>
/// 人员与部门的多对多关系。
/// </summary>
public sealed class PersonDepartmentEntity
{
    /// <summary>
    /// 人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;
    /// <summary>
    /// 部门 ID。
    /// </summary>
    public string DepartmentId { get; set; } = string.Empty;
    /// <summary>
    /// 是否为主部门。
    /// </summary>
    public bool IsPrimary
    {
        get; set;
    }
}

/// <summary>
/// 组织角色。
/// </summary>
public sealed class OrganizationRoleEntity
{
    /// <summary>
    /// 角色编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>
    /// 角色名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 人员拥有的角色。
/// </summary>
public sealed class PersonRoleEntity
{
    /// <summary>
    /// 人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;
    /// <summary>
    /// 角色编码。
    /// </summary>
    public string RoleCode { get; set; } = string.Empty;
}

/// <summary>
/// 接入应用的外部人员 ID 绑定。
/// </summary>
public sealed class ExternalPersonBindingEntity
{
    /// <summary>
    /// 接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;
    /// <summary>
    /// 业务系统人员 ID。
    /// </summary>
    public string ExternalUserId { get; set; } = string.Empty;
    /// <summary>
    /// 审批服务人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;
    /// <summary>
    /// 绑定方式。
    /// </summary>
    public string Method { get; set; } = string.Empty;
}

/// <summary>
/// 接入应用的外部部门 ID 绑定。
/// </summary>
public sealed class ExternalDepartmentBindingEntity
{
    /// <summary>
    /// 接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;
    /// <summary>
    /// 业务系统部门 ID。
    /// </summary>
    public string ExternalDepartmentId { get; set; } = string.Empty;
    /// <summary>
    /// 审批服务部门 ID。
    /// </summary>
    public string DepartmentId { get; set; } = string.Empty;
    /// <summary>
    /// 绑定方式。
    /// </summary>
    public string Method { get; set; } = string.Empty;
}

/// <summary>
/// 每次组织变更的审计记录。
/// </summary>
public sealed class OrganizationChangeEntity
{
    /// <summary>
    /// 变更 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 实体种类。
    /// </summary>
    public string EntityKind { get; set; } = string.Empty;
    /// <summary>
    /// 实体 ID。
    /// </summary>
    public string EntityId { get; set; } = string.Empty;
    /// <summary>
    /// 操作人员 ID。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// 来源编码。
    /// </summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>
    /// 变更种类。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>
    /// 变更前的 JSON 快照。
    /// </summary>
    public string? BeforeJson
    {
        get; set;
    }
    /// <summary>
    /// 变更后的 JSON 快照。
    /// </summary>
    public string? AfterJson
    {
        get; set;
    }
    /// <summary>
    /// 发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}

/// <summary>
/// 离职交接提醒；管理员确认前保留，复职后取消过期提醒。
/// </summary>
public sealed class DepartureNoticeEntity
{
    /// <summary>
    /// 离职人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;
    /// <summary>
    /// 生成时间。
    /// </summary>
    public DateTimeOffset CreatedAt
    {
        get; set;
    }
    /// <summary>
    /// 尚未完成的待办数量。
    /// </summary>
    public int PendingTaskCount
    {
        get; set;
    }
    /// <summary>
    /// 管理员完成交接的时间。
    /// </summary>
    public DateTimeOffset? AcknowledgedAt
    {
        get; set;
    }
}
