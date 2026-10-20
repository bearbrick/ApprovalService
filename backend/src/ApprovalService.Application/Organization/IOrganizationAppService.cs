using ApprovalService.Domain.Organization;

namespace ApprovalService.Application.Organization;

/// <summary>
/// 组织导入、变更推送和业务系统绑定的用例入口。
/// </summary>
public interface IOrganizationAppService
{
    /// <summary>
    /// 在写入前一次收集全部组织结构问题。
    /// </summary>
    /// <param name="batch">导入数据。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>问题列表。</returns>
    Task<IReadOnlyList<OrganizationImportIssue>> ValidateImportAsync(OrganizationImportBatch batch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入来自审批服务的可信组织数据。
    /// </summary>
    /// <param name="batch">导入数据。</param>
    /// <param name="actorId">管理员 ID。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>校验问题；空集合表示已写入。</returns>
    Task<IReadOnlyList<OrganizationImportIssue>> ImportAsync(OrganizationImportBatch batch, string actorId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理接入应用的组织变更；不会覆盖其他可信来源的字段。
    /// </summary>
    /// <param name="batch">变更数据。</param>
    /// <param name="clientAppCode">已鉴权的接入应用编码。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>校验问题和离职交接人员 ID。</returns>
    Task<OrganizationPushResult> PushChangesAsync(OrganizationImportBatch batch, string clientAppCode,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工号或手机号自动匹配，或用管理员指定 ID 完成人员绑定。
    /// </summary>
    /// <param name="input">绑定请求。</param>
    /// <param name="actorId">操作者 ID。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>绑定的内部人员 ID。</returns>
    Task<string> BindPersonAsync(PersonBindingInput input, string actorId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 绑定业务系统部门 ID 到内部部门。
    /// </summary>
    /// <param name="input">绑定请求。</param>
    /// <param name="actorId">操作者 ID。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    Task BindDepartmentAsync(DepartmentBindingInput input, string actorId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 推送变更的校验与离职提醒结果。
/// </summary>
public sealed class OrganizationPushResult
{
    /// <summary>所有校验问题。</summary>
    public IReadOnlyList<OrganizationImportIssue> Issues { get; set; } = [];
    /// <summary>需要管理员交接的离职人员 ID。</summary>
    public IReadOnlyList<string> DepartedPersonIds { get; set; } = [];
}

/// <summary>
/// 人员绑定输入。
/// </summary>
public sealed class PersonBindingInput
{
    /// <summary>接入应用编码。</summary>
    public string ClientAppCode { get; set; } = string.Empty;
    /// <summary>业务系统人员 ID。</summary>
    public string ExternalUserId { get; set; } = string.Empty;
    /// <summary>管理员指定的内部人员 ID。</summary>
    public string? PersonId { get; set; }
    /// <summary>自动匹配工号。</summary>
    public string? EmployeeNumber { get; set; }
    /// <summary>自动匹配手机号。</summary>
    public string? Phone { get; set; }
    /// <summary>绑定方法：automatic、import、push 或 manual。</summary>
    public string Method { get; set; } = "automatic";
}

/// <summary>
/// 部门绑定输入。
/// </summary>
public sealed class DepartmentBindingInput
{
    /// <summary>接入应用编码。</summary>
    public string ClientAppCode { get; set; } = string.Empty;
    /// <summary>业务系统部门 ID。</summary>
    public string ExternalDepartmentId { get; set; } = string.Empty;
    /// <summary>内部部门 ID。</summary>
    public string DepartmentId { get; set; } = string.Empty;
    /// <summary>绑定方法：import、push 或 manual。</summary>
    public string Method { get; set; } = "manual";
}
