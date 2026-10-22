namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 审批单状态与事件的事务仓储接口，不向应用层暴露数据库框架。
/// </summary>
public interface IApprovalRuntimeRepository
{
    /// <summary>
    /// 检查当前数据库是否可连接，供就绪探针使用。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>数据库可连接为真。</returns>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取审批单、固定流程版本和最新操作记录。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>运行快照；不存在时为空。</returns>
    Task<RuntimeSnapshot?> LoadAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取已发布的流程版本。
    /// </summary>
    /// <param name="versionId">流程版本 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>流程版本；不存在时为空。</returns>
    Task<FlowVersionEntity?> LoadVersionAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询指定人员在进行中审批单上的待处理或排队任务。
    /// </summary>
    /// <param name="assigneeId">已停用人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>按审批单和任务 ID 排序的候选任务。</returns>
    Task<IReadOnlyList<DepartureTaskRef>> ListDepartureTasksAsync(string assigneeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在一个事务中保存状态、任务、操作记录和待发项。
    /// </summary>
    /// <param name="commit">本次命令的全部变更。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>提交成功为真；乐观锁冲突为假。</returns>
    Task<bool> CommitAsync(RuntimeCommit commit, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在同一数据库快照中读取审批单事件锚点与全部操作记录。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>事件链快照；审批单不存在时为空。</returns>
    Task<EventChainSnapshot?> LoadEventChainAsync(string instanceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 离职扫描得到的在途任务定位信息，不作为直接写入任务投影的命令。
/// </summary>
public sealed class DepartureTaskRef
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 人工任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
}

/// <summary>
/// 审批单事件锚点和操作记录的一致读取结果。
/// </summary>
public sealed class EventChainSnapshot
{
    /// <summary>
    /// 审批单最后一条事件的顺序号。
    /// </summary>
    public long LastEventSequence
    {
        get; set;
    }

    /// <summary>
    /// 审批单最后一条事件的校验值。
    /// </summary>
    public string LastEventHash { get; set; } = string.Empty;

    /// <summary>
    /// 同一数据库快照内按顺序号升序读取的全部操作记录。
    /// </summary>
    public IReadOnlyList<OperationEventEntity> Events { get; set; } = [];
}

/// <summary>
/// 一张审批单以及发起时锁定的流程版本。
/// </summary>
public sealed class RuntimeSnapshot
{
    /// <summary>
    /// 审批单当前状态。
    /// </summary>
    public InstanceEntity Instance { get; set; } = new();

    /// <summary>
    /// 发起时锁定的流程版本。
    /// </summary>
    public FlowVersionEntity FlowVersion { get; set; } = new();

    /// <summary>
    /// 最新操作记录；审批单尚无事件时为空。
    /// </summary>
    public OperationEventEntity? LastEvent
    {
        get; set;
    }
}

/// <summary>
/// 一条命令在数据库事务内必须一同落库的全部变更。
/// </summary>
public sealed class RuntimeCommit
{
    /// <summary>
    /// 提交后的审批单状态。
    /// </summary>
    public InstanceEntity Instance { get; set; } = new();

    /// <summary>
    /// 读取时的乐观锁版本；新建审批单时为空。
    /// </summary>
    public long? ExpectedVersion
    {
        get; set;
    }

    /// <summary>
    /// 命令执行后的所有任务投影。
    /// </summary>
    public IReadOnlyList<ApprovalTaskEntity> Tasks { get; set; } = [];

    /// <summary>
    /// 本次新增的只追加操作记录。
    /// </summary>
    public IReadOnlyList<OperationEventEntity> Events { get; set; } = [];

    /// <summary>
    /// 与事件同事务写入的待发项。
    /// </summary>
    public IReadOnlyList<OutboxItemEntity> OutboxItems { get; set; } = [];

    /// <summary>
    /// 与发起审批同事务关联的接入应用附件。
    /// </summary>
    public IReadOnlyList<RuntimeFileLink> FileLinks { get; set; } = [];
}

/// <summary>
/// 一份待在审批提交事务中核对归属并关联的附件。
/// </summary>
public sealed class RuntimeFileLink
{
    /// <summary>
    /// 已上传文件 ID。
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// 已认证接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;

    /// <summary>
    /// 发起命令的固定 UTC 时间。
    /// </summary>
    public DateTimeOffset LinkedAt
    {
        get; set;
    }
}
