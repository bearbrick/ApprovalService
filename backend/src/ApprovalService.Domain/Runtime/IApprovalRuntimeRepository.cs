namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 审批单状态与事件的事务仓储接口，不向应用层暴露数据库框架。
/// </summary>
public interface IApprovalRuntimeRepository
{
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
    /// 在一个事务中保存状态、任务、操作记录和待发项。
    /// </summary>
    /// <param name="commit">本次命令的全部变更。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>提交成功为真；乐观锁冲突为假。</returns>
    Task<bool> CommitAsync(RuntimeCommit commit, CancellationToken cancellationToken = default);

    /// <summary>
    /// 顺序读取只追加的操作记录，用于校验篡改。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>按顺序号升序排列的记录。</returns>
    Task<IReadOnlyList<OperationEventEntity>> LoadEventsAsync(string instanceId, CancellationToken cancellationToken = default);
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
    public OperationEventEntity? LastEvent { get; set; }
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
    public long? ExpectedVersion { get; set; }

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
}
