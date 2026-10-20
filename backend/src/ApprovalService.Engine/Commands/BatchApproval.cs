using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 批量同意的一项独立审批单输入。
/// </summary>
public sealed class BatchApprovalItem
{
    /// <summary>
    /// 已加载的审批单状态。
    /// </summary>
    public EngineState State { get; set; } = new();
    /// <summary>
    /// 同意命令及实际操作人。
    /// </summary>
    public EngineCommand Command { get; set; } = new();
    /// <summary>
    /// 此审批单的固定执行上下文。
    /// </summary>
    public EngineContext Context { get; set; } = new();
}

/// <summary>
/// 在持久化前纯计算整批同意；任一项失败时不返回部分结果。
/// </summary>
public static class BatchApproval
{
    /// <summary>
    /// 逐项调用唯一命令入口计算批量同意。
    /// </summary>
    /// <param name="items">不同审批单的待办及上下文。</param>
    /// <returns>和输入顺序一致的全部结果，供应用层在事务中保存。</returns>
    public static IReadOnlyList<EngineResult> Execute(IReadOnlyList<BatchApprovalItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "批量同意至少包含一项任务。");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<EngineResult>(items.Count);
        string? actorId = null;
        foreach (var item in items)
        {
            if (!unique.Add(item.State.InstanceId))
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "一批操作中不能重复包含同一审批单。");
            }

            if (item.Command.Kind != EngineCommandKinds.Approve || item.Command.Operation is null)
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "批量同意只接受带操作人的同意命令。");
            }

            actorId ??= item.Command.Operation.ActorId;
            if (actorId != item.Command.Operation.ActorId)
            {
                throw new EngineException(ErrorCodes.ValidationFailed, "一批审批只能由同一操作人执行。");
            }

            // 输入来自调用方，先复制再打批量标记，避免修改原命令。
            var command = new EngineCommand
            {
                Kind = item.Command.Kind,
                RequestId = item.Command.RequestId,
                InstanceId = item.Command.InstanceId,
                TaskId = item.Command.TaskId,
                Operation = new OperationInput
                {
                    ActorId = item.Command.Operation.ActorId,
                    Opinion = item.Command.Operation.Opinion,
                    IsBulkApproval = true,
                },
            };
            results.Add(EngineRuntime.Execute(item.State, command, item.Context));
        }

        return results;
    }
}
