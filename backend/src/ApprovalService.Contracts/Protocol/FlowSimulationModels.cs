using System.Text.Json;
using ApprovalService.Contracts.Flows;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 使用尚未发布的完整草稿和假输入创建模拟会话。
/// </summary>
public sealed class FlowSimulationCreateRequest
{
    /// <summary>
    /// 完整流程草稿。
    /// </summary>
    public FlowDefinition? Definition
    {
        get; set;
    }

    /// <summary>
    /// 假审批数据对象。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }

    /// <summary>
    /// 假发起人内部 ID。
    /// </summary>
    public string InitiatorId { get; set; } = string.Empty;

    /// <summary>
    /// 各人工节点的假处理人内部 ID，按审批顺序排列。
    /// </summary>
    public Dictionary<string, List<string>> FakeAssignees { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 模拟一步；自动动作和子流程结果也必须由调用方显式提供。
/// </summary>
public sealed class FlowSimulationStepRequest
{
    /// <summary>
    /// 只允许当前任务完成动作、拒绝、自动动作或子流程完成。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 待处理的假任务 ID。
    /// </summary>
    public string? TaskId
    {
        get; set;
    }

    /// <summary>
    /// 待完成的自动动作或子流程令牌 ID。
    /// </summary>
    public string? TokenId
    {
        get; set;
    }

    /// <summary>
    /// 假操作人员 ID，必须是该任务的假处理人。
    /// </summary>
    public string? ActorId
    {
        get; set;
    }

    /// <summary>
    /// 子流程假结果是否成功。
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// 自动动作成功的可选假结果。
    /// </summary>
    public JsonElement? ActionResult
    {
        get; set;
    }
}

/// <summary>
/// 不含真实审批单的模拟状态。
/// </summary>
public sealed class FlowSimulationResponse
{
    /// <summary>
    /// 随机会话 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 所属流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 闲置过期时间。
    /// </summary>
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }

    /// <summary>
    /// 当前运行状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 目前所有假任务及其状态。
    /// </summary>
    public List<FlowSimulationTask> Tasks { get; set; } = [];

    /// <summary>
    /// 等待自动动作或子流程假结果的令牌。
    /// </summary>
    public List<FlowSimulationWait> Waits { get; set; } = [];

    /// <summary>
    /// 截至当前步骤的内存事件。
    /// </summary>
    public List<FlowSimulationEvent> Events { get; set; } = [];
}

/// <summary>
/// 模拟任务摘要。
/// </summary>
public sealed class FlowSimulationTask
{
    /// <summary>
    /// 假任务 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 假处理人 ID。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;

    /// <summary>
    /// 任务状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 成功推进的命令种类。
    /// </summary>
    public string CompletionKind { get; set; } = string.Empty;
}

/// <summary>
/// 等待假外部结论的令牌。
/// </summary>
public sealed class FlowSimulationWait
{
    /// <summary>
    /// 令牌 ID。
    /// </summary>
    public string TokenId { get; set; } = string.Empty;

    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 等待种类：action 或 subprocess。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
}

/// <summary>
/// 模拟会话内的纯引擎事件摘要。
/// </summary>
public sealed class FlowSimulationEvent
{
    /// <summary>
    /// 事件种类。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 相关节点 ID。
    /// </summary>
    public string? NodeId
    {
        get; set;
    }

    /// <summary>
    /// 相关任务 ID。
    /// </summary>
    public string? TaskId
    {
        get; set;
    }

    /// <summary>
    /// 相关令牌 ID。
    /// </summary>
    public string? TokenId
    {
        get; set;
    }
}
