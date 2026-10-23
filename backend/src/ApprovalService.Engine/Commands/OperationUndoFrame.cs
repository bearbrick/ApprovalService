using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 撤销同意的固定前态及同意者身份。
/// </summary>
public sealed class OperationUndoFrame
{
    /// <summary>
    /// 同意命令之前的状态快照，不携带更早的撤销帧。
    /// </summary>
    public EngineState Snapshot { get; set; } = new();
    /// <summary>
    /// 允许收回同意的实际操作人。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>
    /// 本次同意任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>
    /// 同意后的累计人工作业次数。
    /// </summary>
    public int DecisionCountAfter
    {
        get; set;
    }
    /// <summary>
    /// 同意命令完成后的已处理请求数，防止撤销时抹掉后续已留痕操作。
    /// </summary>
    public int ProcessedRequestCountAfter
    {
        get; set;
    }
}
