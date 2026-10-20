using ApprovalService.Domain.Runtime;

namespace ApprovalService.Domain.Integration;

/// <summary>
/// 开放接口所需的审批单与待办只读投影，不暴露数据库查询框架。
/// </summary>
public interface IApprovalOpenQueryRepository
{
    /// <summary>
    /// 按审批单 ID 读取当前快照。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>审批单；不存在时为空。</returns>
    Task<InstanceEntity?> LoadInstanceAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取同流程、同业务单号的在途审批单，兼容迁移前已经在途的历史记录。
    /// </summary>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>在途审批单或空。</returns>
    Task<InstanceEntity?> LoadActiveAsync(string flowCode, string businessKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取一个业务单号的所有历史审批单，最新的排在前面。
    /// </summary>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="clientAppCode">来源接入应用编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>历史审批单。</returns>
    Task<IReadOnlyList<InstanceEntity>> ListInstancesAsync(string flowCode, string businessKey, string clientAppCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取一张审批单的任务和审计事件。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>按发生顺序排列的详情。</returns>
    Task<OpenInstanceDetails> LoadDetailsAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取待办任务与对应的审批单、锁定流程版本。
    /// </summary>
    /// <param name="personId">已解析的内部人员 ID。</param>
    /// <param name="clientAppCode">限定来源接入应用；为空表示全部。</param>
    /// <param name="skip">跳过的任务数。</param>
    /// <param name="take">最多读取的任务数。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>待办详情。</returns>
    Task<IReadOnlyList<OpenTaskRecord>> ListPendingTasksAsync(string personId, string? clientAppCode,
        int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// 直接在数据库统计待办、加急和超时数量。
    /// </summary>
    /// <param name="personId">已解析的内部人员 ID。</param>
    /// <param name="clientAppCode">限定来源接入应用；为空表示全部。</param>
    /// <param name="now">判断超时的当前时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>三类数量。</returns>
    Task<OpenTaskCounts> CountPendingTasksAsync(string personId, string? clientAppCode, DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一张审批单的任务与事件详情。
/// </summary>
public sealed class OpenInstanceDetails
{
    /// <summary>
    /// 人工任务投影。
    /// </summary>
    public IReadOnlyList<ApprovalTaskEntity> Tasks { get; set; } = [];

    /// <summary>
    /// 顺序排列的操作记录。
    /// </summary>
    public IReadOnlyList<OperationEventEntity> Events { get; set; } = [];
}

/// <summary>
/// 一条待办与其审批单、流程版本。
/// </summary>
public sealed class OpenTaskRecord
{
    /// <summary>
    /// 任务。
    /// </summary>
    public ApprovalTaskEntity Task { get; set; } = new();

    /// <summary>
    /// 审批单。
    /// </summary>
    public InstanceEntity Instance { get; set; } = new();

    /// <summary>
    /// 发起时锁定的流程版本。
    /// </summary>
    public FlowVersionEntity FlowVersion { get; set; } = new();
}

/// <summary>
/// 待办数量的数据库汇总。
/// </summary>
public sealed class OpenTaskCounts
{
    /// <summary>
    /// 待处理数量。
    /// </summary>
    public int Pending
    {
        get; set;
    }

    /// <summary>
    /// 待处理且加急的数量。
    /// </summary>
    public int Urgent
    {
        get; set;
    }

    /// <summary>
    /// 待处理且已过期的数量。
    /// </summary>
    public int Overdue
    {
        get; set;
    }
}
