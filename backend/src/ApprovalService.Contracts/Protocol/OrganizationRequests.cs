namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 部门变更输入。
/// </summary>
/// <param name="Id">审批服务内部部门 ID。</param>
/// <param name="Code">部门编码。</param>
/// <param name="Name">部门名称。</param>
/// <param name="ParentId">上级部门 ID。</param>
/// <param name="HeadPersonId">负责人内部人员 ID。</param>
public sealed record DepartmentInput(string Id, string Code, string Name, string? ParentId, string? HeadPersonId);

/// <summary>
/// 人员变更输入。
/// </summary>
/// <param name="Id">审批服务内部人员 ID。</param>
/// <param name="EmployeeNumber">唯一工号。</param>
/// <param name="Name">姓名。</param>
/// <param name="Phone">手机号。</param>
/// <param name="Email">邮箱。</param>
/// <param name="ManagerId">直属上级内部人员 ID。</param>
/// <param name="Position">岗位编码。</param>
/// <param name="Grade">职级编码。</param>
/// <param name="GradeRank">职级序数。</param>
/// <param name="IsActive">是否在职。</param>
public sealed record PersonInput(string Id, string EmployeeNumber, string Name, string? Phone,
    string? Email, string? ManagerId, string? Position, string? Grade, int GradeRank, bool IsActive);

/// <summary>
/// 人员所在部门输入。
/// </summary>
/// <param name="PersonId">内部人员 ID。</param>
/// <param name="DepartmentId">内部部门 ID。</param>
/// <param name="IsPrimary">是否主部门。</param>
public sealed record PersonDepartmentInput(string PersonId, string DepartmentId, bool IsPrimary);

/// <summary>
/// 组织变更或导入前检查的完整批次。
/// </summary>
/// <param name="Departments">部门列表。</param>
/// <param name="People">人员列表。</param>
/// <param name="Memberships">人员部门关系列表。</param>
public sealed record OrganizationBatchRequest(IReadOnlyList<DepartmentInput> Departments,
    IReadOnlyList<PersonInput> People, IReadOnlyList<PersonDepartmentInput> Memberships);

/// <summary>
/// 绑定业务系统标识的输入。
/// </summary>
/// <param name="Kind">person 或 department。</param>
/// <param name="ExternalId">业务系统内部标识。</param>
/// <param name="InternalId">审批服务内部标识；按工号或手机号自动匹配人员时可空。</param>
/// <param name="EmployeeNumber">人员自动匹配工号。</param>
/// <param name="Phone">人员自动匹配手机号。</param>
/// <param name="Method">automatic、import、push 或 manual。</param>
/// <param name="ClientAppCode">管理端指定的接入应用编码；开放接口忽略此值。</param>
public sealed record OrganizationBindingRequest(string Kind, string ExternalId, string? InternalId,
    string? EmployeeNumber, string? Phone, string Method, string? ClientAppCode);

/// <summary>
/// 组织批次处理结果。
/// </summary>
/// <param name="Applied">是否已写入；预检查时始终为 false。</param>
/// <param name="Issues">一次收集的全部字段问题。</param>
/// <param name="DepartedPersonIds">需要管理员处理交接的离职人员 ID。</param>
public sealed record OrganizationBatchResponse(bool Applied, IReadOnlyList<ApiFieldError> Issues,
    IReadOnlyList<string> DepartedPersonIds);
