using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Runtime;

/// <summary>
/// 把引擎命令和数据库事务连接起来的应用服务。
/// </summary>
public interface IApprovalRuntimeAppService
{
    /// <summary>
    /// 按审批单锁定的流程版本执行命令并持久化结果。
    /// </summary>
    /// <param name="input">命令和已解析的人员、条件快照。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>引擎结果；同一请求重复提交时标记为重放。</returns>
    Task<EngineResult> ExecuteAsync(RuntimeCommandInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已停用人员的在途任务候选，实际变更仍需逐项调用统一引擎入口。
    /// </summary>
    /// <param name="departedPersonId">已停用人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>在途任务定位信息。</returns>
    Task<IReadOnlyList<DepartureTaskRef>> ListDepartureTasksAsync(string departedPersonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按发布节点的离职策略重分配一项在途任务，并原子保存事件和任务投影。
    /// </summary>
    /// <param name="task">扫描得到的候选任务。</param>
    /// <param name="departedPersonId">组织来源已确认停用的人员。</param>
    /// <param name="snapshot">该审批单的当前组织快照，含流程管理员及上级链。</param>
    /// <param name="occurredAt">本次变更的 UTC 时间。</param>
    /// <param name="requestId">稳定请求 ID，重试时须复用。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>引擎结果，重复请求标记为重放。</returns>
    Task<EngineResult> ReassignDepartedTaskAsync(DepartureTaskRef task, string departedPersonId,
        AssigneeSnapshot snapshot, DateTimeOffset occurredAt, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验某张审批单的事件链是否被改写。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>顺序及所有校验值均完整为真。</returns>
    Task<bool> VerifyEventChainAsync(string instanceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 应用服务命令输入，外部查询得到的人员与条件结果由调用方显式提供。
/// </summary>
public sealed class RuntimeCommandInput
{
    /// <summary>
    /// 管理员页面读取的运行修订，空值沿用普通运行行为。
    /// </summary>
    public long? ExpectedInstanceVersion
    {
        get; set;
    }

    /// <summary>
    /// 已核实的本地干预人员，供最终事务重验权限。
    /// </summary>
    public string? InterventionActorId
    {
        get; set;
    }

    /// <summary>
    /// 最终事务须重验在职状态的接手人员。
    /// </summary>
    public string? InterventionTargetId
    {
        get; set;
    }

    /// <summary>
    /// 唯一引擎命令。
    /// </summary>
    public EngineCommand Command { get; set; } = new();

    /// <summary>
    /// 纯引擎的环境输入；执行图由持久化的发布版本覆盖。
    /// </summary>
    public EngineContext Context { get; set; } = new();

    /// <summary>
    /// 开始命令的新审批单元数据；其他命令留空。
    /// </summary>
    public InstanceEntity? StartInstance
    {
        get; set;
    }

    /// <summary>
    /// 重新提交时由应用层校验并计算后的审批数据；与状态事件在同一事务保存。
    /// </summary>
    public string? UpdatedApprovalDataJson
    {
        get; set;
    }

    /// <summary>
    /// 审批数据人员/部门标识的可信内部映射；与数据一同更新。
    /// </summary>
    public string? UpdatedRoutingInputsJson
    {
        get; set;
    }

    /// <summary>
    /// 发起时必须与审批单、任务和事件同事务提交的附件关联。
    /// </summary>
    public IReadOnlyList<RuntimeFileLink> FileLinks { get; set; } = [];
}
