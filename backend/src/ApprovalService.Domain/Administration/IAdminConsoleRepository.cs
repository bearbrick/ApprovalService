namespace ApprovalService.Domain.Administration;

/// <summary>
/// 管理后台对绑定和审计元数据的只读查询边界。
/// </summary>
public interface IAdminConsoleRepository
{
    /// <summary>
    /// 读取全部业务系统标识绑定，不包含人员联系方式。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>绑定摘要。</returns>
    Task<IReadOnlyList<AdminBindingRecord>> ListBindingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取最近的组织及接入应用操作记录，不读取完整前后数据快照。
    /// </summary>
    /// <param name="limit">最多读取条数。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>审计摘要。</returns>
    Task<IReadOnlyList<AdminAuditRecord>> ListAuditAsync(int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// 业务系统与内部人员或部门的绑定摘要。
/// </summary>
public sealed class AdminBindingRecord
{
    /// <summary>
    /// 接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;
    /// <summary>
    /// person 或 department。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>
    /// 业务系统外部 ID。
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;
    /// <summary>
    /// 审批服务内部人员或部门 ID。
    /// </summary>
    public string InternalId { get; set; } = string.Empty;
    /// <summary>
    /// 绑定来源。
    /// </summary>
    public string Method { get; set; } = string.Empty;
}

/// <summary>
/// 不含组织完整快照的操作日志摘要。
/// </summary>
public sealed class AdminAuditRecord
{
    /// <summary>
    /// 操作时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
    /// <summary>
    /// 操作者人员 ID 或应用标识。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// 对象类型。
    /// </summary>
    public string EntityKind { get; set; } = string.Empty;
    /// <summary>
    /// 对象 ID。
    /// </summary>
    public string EntityId { get; set; } = string.Empty;
    /// <summary>
    /// 操作类型。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>
    /// 明确填写的操作原因。
    /// </summary>
    public string? Reason
    {
        get; set;
    }
}
