namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 流程草稿、发布版和管理授权的持久化边界。
/// </summary>
public interface IFlowManagementRepository
{
    /// <summary>
    /// 读取当前操作人的组织角色与直接所属部门。
    /// </summary>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前身份快照。</returns>
    Task<FlowManagerIdentity> LoadIdentityAsync(string personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出流程与草稿修订，供应用层按当前身份筛选。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>流程快照。</returns>
    Task<IReadOnlyList<ManagedFlowSnapshot>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按稳定编码读取流程及草稿。
    /// </summary>
    /// <param name="code">流程编码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>存在时的流程快照。</returns>
    Task<ManagedFlowSnapshot?> LoadAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在一个事务中创建流程、初始草稿和安全审计。
    /// </summary>
    /// <param name="input">已通过应用层语义检查的草稿写入。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>新建后的快照。</returns>
    Task<ManagedFlowSnapshot> CreateAsync(FlowDraftWrite input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按预期修订号替换草稿和可编辑元数据。
    /// </summary>
    /// <param name="input">带乐观并发前提的草稿写入。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>修改后的快照。</returns>
    Task<ManagedFlowSnapshot> ReplaceDraftAsync(FlowDraftWrite input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重新核对草稿与当前发布指针并原子追加不可变版本。
    /// </summary>
    /// <param name="input">已校验执行图及并发前提。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>新发布后的快照。</returns>
    Task<ManagedFlowSnapshot> PublishAsync(FlowPublishWrite input, CancellationToken cancellationToken = default);
}

/// <summary>
/// 当前人员的流程管理授权所需组织数据。
/// </summary>
public sealed class FlowManagerIdentity
{
    /// <summary>
    /// 是否在职。
    /// </summary>
    public bool IsActive
    {
        get; set;
    }

    /// <summary>
    /// 当前组织管理员角色。
    /// </summary>
    public bool IsOrganizationAdmin
    {
        get; set;
    }

    /// <summary>
    /// 当前流程管理员角色。
    /// </summary>
    public bool IsFlowAdmin
    {
        get; set;
    }

    /// <summary>
    /// 直接加入的部门 ID。
    /// </summary>
    public IReadOnlyList<string> DirectDepartmentIds { get; set; } = [];
}

/// <summary>
/// 流程元数据、草稿及当前已发布版本的同次读取快照。
/// </summary>
public sealed class ManagedFlowSnapshot
{
    /// <summary>
    /// 流程元数据。
    /// </summary>
    public FlowEntity Flow { get; set; } = new();

    /// <summary>
    /// 可编辑草稿。
    /// </summary>
    public FlowDraftEntity? Draft
    {
        get; set;
    }

    /// <summary>
    /// 当前发布版。
    /// </summary>
    public FlowVersionEntity? Published
    {
        get; set;
    }
}

/// <summary>
/// 创建或替换流程草稿的可信输入。
/// </summary>
public sealed class FlowDraftWrite
{
    /// <summary>
    /// 流程 ID；仅创建时使用。
    /// </summary>
    public string FlowId { get; set; } = string.Empty;

    /// <summary>
    /// 稳定流程编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 流程显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 流程分组。
    /// </summary>
    public string? Group
    {
        get; set;
    }

    /// <summary>
    /// 流程直接归属部门 ID。
    /// </summary>
    public string? OwnerDepartmentId
    {
        get; set;
    }

    /// <summary>
    /// 运行期指定管理员内部人员 ID。
    /// </summary>
    public string? AdministratorId
    {
        get; set;
    }

    /// <summary>
    /// 完整草稿 JSON。
    /// </summary>
    public string DefinitionJson { get; set; } = string.Empty;

    /// <summary>
    /// 草稿内容 SHA-256。
    /// </summary>
    public string ContentSha256 { get; set; } = string.Empty;

    /// <summary>
    /// 预期草稿修订；仅替换时使用。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 已验证的当前操作人员 ID。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// 是否由本地账号会话发起。
    /// </summary>
    public bool IsLocalSession
    {
        get; set;
    }

    /// <summary>
    /// 管理员填写的操作原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 操作时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }

    /// <summary>
    /// 审计记录 ID。
    /// </summary>
    public string AuditId { get; set; } = string.Empty;
}

/// <summary>
/// 发布流程时已编译的可信输入。
/// </summary>
public sealed class FlowPublishWrite
{
    /// <summary>
    /// 流程定义格式版本。
    /// </summary>
    public int SchemaVersion
    {
        get; set;
    }

    /// <summary>
    /// 稳定流程编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 预期草稿修订。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 预期当前发布版本 ID；首次发布时为空。
    /// </summary>
    public string? ExpectedPublishedVersionId
    {
        get; set;
    }

    /// <summary>
    /// 与编译执行图一致的原草稿 JSON。
    /// </summary>
    public string DefinitionJson { get; set; } = string.Empty;

    /// <summary>
    /// 已编译执行图 JSON。
    /// </summary>
    public string GraphJson { get; set; } = string.Empty;

    /// <summary>
    /// 流程定义内容 SHA-256。
    /// </summary>
    public string ContentSha256 { get; set; } = string.Empty;

    /// <summary>
    /// 已验证的当前操作人员 ID。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// 是否由本地账号会话发起。
    /// </summary>
    public bool IsLocalSession
    {
        get; set;
    }

    /// <summary>
    /// 管理员填写的操作原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 发布版本 ID。
    /// </summary>
    public string VersionId { get; set; } = string.Empty;

    /// <summary>
    /// 审计记录 ID。
    /// </summary>
    public string AuditId { get; set; } = string.Empty;

    /// <summary>
    /// 发布时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}
