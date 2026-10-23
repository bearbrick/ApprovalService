using System.Text.Json;

namespace ApprovalService.Engine.Execution;

/// <summary>
/// 引擎产生的一条只追加事件，应用层负责持久化及出站派发。
/// </summary>
public sealed class EngineEvent
{
    /// <summary>
    /// 确定性事件 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 事件种类。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>
    /// 发生时间，由调用方提供。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
    /// <summary>
    /// 相关节点 ID。
    /// </summary>
    public string? NodeId
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
    /// <summary>
    /// 相关任务 ID。
    /// </summary>
    public string? TaskId
    {
        get; set;
    }
    /// <summary>
    /// 子流程编码或自动动作编码。
    /// </summary>
    public string? TargetCode
    {
        get; set;
    }
    /// <summary>
    /// 自动动作完成时的可选业务结果；审批数据快照不包含此值。
    /// </summary>
    public JsonElement? ActionResult
    {
        get; set;
    }
    /// <summary>
    /// 预览时的人员，仅人员变化事件填入。
    /// </summary>
    public List<string> PreviewAssigneeIds { get; set; } = [];
    /// <summary>
    /// 到节点时重新解析的人员，仅人员变化事件填入。
    /// </summary>
    public List<string> ResolvedAssigneeIds { get; set; } = [];

    /// <summary>
    /// 操作人的身份、代办关系、意见与干预原因。
    /// </summary>
    public ApprovalService.Engine.Commands.OperationAudit? Audit
    {
        get; set;
    }
}

/// <summary>
/// 一次命令的纯计算结果。
/// </summary>
public sealed class EngineResult
{
    /// <summary>
    /// 新状态。
    /// </summary>
    public EngineState State { get; set; } = new();
    /// <summary>
    /// 本命令产生的新事件；幂等重放为空。
    /// </summary>
    public List<EngineEvent> Events { get; set; } = [];
    /// <summary>
    /// 同一请求 ID 已处理过时为真。
    /// </summary>
    public bool IsReplay
    {
        get; set;
    }

    /// <summary>
    /// 本次纯计算实际到达的业务取人节点；有值时不得提交中间状态。
    /// </summary>
    public BusinessAssigneeRequirement? RequiredBusinessAssignee
    {
        get; set;
    }
}

/// <summary>
/// 纯引擎在实际进入人工节点时请求调用方预取业务人员。
/// </summary>
public sealed class BusinessAssigneeRequirement
{
    /// <summary>
    /// 实际到达的节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 节点配置的业务来源值；可能为空。
    /// </summary>
    public string? SourceValue
    {
        get; set;
    }
}

/// <summary>
/// 可由调用方纠正的无效命令或运行图。
/// </summary>
public sealed class EngineException : Exception
{
    /// <summary>
    /// 构造带稳定错误码和中文说明的错误。
    /// </summary>
    /// <param name="code">错误码。</param>
    /// <param name="message">中文说明。</param>
    public EngineException(string code, string message) : base(message) => Code = code;

    /// <summary>
    /// 错误码。
    /// </summary>
    public string Code
    {
        get;
    }
}
