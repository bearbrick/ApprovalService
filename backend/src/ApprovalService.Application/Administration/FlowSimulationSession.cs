using System.Text.Json;
using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 一场仅在当前进程中有效的假审批运行状态。
/// </summary>
public sealed class FlowSimulationSession
{
    /// <summary>
    /// 同步保护对象。
    /// </summary>
    public object Sync { get; } = new();

    /// <summary>
    /// 随机会话 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 发起模拟的本地管理员内部 ID。
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 本次模拟的执行图副本。
    /// </summary>
    public FlowExecutionGraph Graph { get; set; } = new();

    /// <summary>
    /// 发起时锁定的假审批字段定义。
    /// </summary>
    public FlowDataDefinition DataDefinition { get; set; } = new();

    /// <summary>
    /// 仅保存在内存中的假业务数据。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }

    /// <summary>
    /// 只含假人员的审批人快照。
    /// </summary>
    public AssigneeSnapshot Assignees { get; set; } = new();

    /// <summary>
    /// 从假数据计算的分支条件。
    /// </summary>
    public Dictionary<string, bool> Conditions { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前纯引擎状态。
    /// </summary>
    public EngineState State { get; set; } = new();

    /// <summary>
    /// 内存事件摘要。
    /// </summary>
    public List<ApprovalService.Contracts.Protocol.FlowSimulationEvent> Events { get; set; } = [];

    /// <summary>
    /// 下一个命令序号。
    /// </summary>
    public int Step
    {
        get; set;
    }

    /// <summary>
    /// 闲置过期时刻。
    /// </summary>
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }
}

/// <summary>
/// 内存模拟会话容器；不得实现数据库持久化。
/// </summary>
public interface IFlowSimulationSessionStore
{
    /// <summary>
    /// 添加新会话。
    /// </summary>
    /// <param name="session">待保存会话。</param>
    /// <param name="now">当前时间，用于清理过期会话。</param>
    void Add(FlowSimulationSession session, DateTimeOffset now);

    /// <summary>
    /// 按 ID 查找当前进程内会话。
    /// </summary>
    /// <param name="id">会话 ID。</param>
    /// <returns>找到时返回会话。</returns>
    FlowSimulationSession? Find(string id);

    /// <summary>
    /// 删除会话。
    /// </summary>
    /// <param name="id">会话 ID。</param>
    void Remove(string id);
}
