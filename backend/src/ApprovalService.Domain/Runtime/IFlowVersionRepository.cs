namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 在当前流程管理范围内读取不可变版本历史并原子恢复旧版草稿。
/// </summary>
public interface IFlowVersionRepository
{
    /// <summary>
    /// 只读取版本摘要，不载入完整定义或执行图。
    /// </summary>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="actorId">当前内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>按版本号倒序的摘要。</returns>
    Task<IReadOnlyList<ManagedFlowVersionSummary>> ListAsync(string code, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default);

    /// <summary>
    /// 同一事务内核对双重并发前提，把指定不可变版本复制为新草稿修订。
    /// </summary>
    /// <param name="input">已检查基本请求语义的恢复输入。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>恢复后的流程快照。</returns>
    Task<ManagedFlowSnapshot> RestoreAsync(FlowVersionRestoreWrite input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 已发布流程版本的安全摘要。
/// </summary>
public sealed class ManagedFlowVersionSummary
{
    /// <summary>
    /// 不可变版本 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 流程内递增版本号。
    /// </summary>
    public int Version
    {
        get; set;
    }

    /// <summary>
    /// 发布时间。
    /// </summary>
    public DateTimeOffset PublishedAt
    {
        get; set;
    }

    /// <summary>
    /// 是否当前新发起所用发布版。
    /// </summary>
    public bool IsCurrent
    {
        get; set;
    }
}

/// <summary>
/// 带草稿及当前发布版双重并发前提的旧版恢复输入。
/// </summary>
public sealed class FlowVersionRestoreWrite
{
    /// <summary>
    /// 稳定流程编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 来源不可变发布版本号。
    /// </summary>
    public int SourceVersion
    {
        get; set;
    }

    /// <summary>
    /// 操作者预期草稿修订。
    /// </summary>
    public long ExpectedRevision
    {
        get; set;
    }

    /// <summary>
    /// 操作者预期当前发布版本 ID。
    /// </summary>
    public string? ExpectedPublishedVersionId
    {
        get; set;
    }

    /// <summary>
    /// 已认证内部人员 ID。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// 是否本地账号会话。
    /// </summary>
    public bool IsLocalSession
    {
        get; set;
    }

    /// <summary>
    /// 管理员填写的原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 新审计记录 ID。
    /// </summary>
    public string AuditId { get; set; } = string.Empty;

    /// <summary>
    /// 操作发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}
