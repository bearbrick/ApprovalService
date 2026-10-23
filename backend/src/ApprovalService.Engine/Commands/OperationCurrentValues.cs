using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Commands;

/// <summary>
/// 在成功推进任务时维护审批人填写字段的当前值。
/// </summary>
internal static class OperationCurrentValues
{
    /// <summary>
    /// 合并本次提交的顶层字段；未提交的字段保持原值。
    /// </summary>
    /// <param name="state">同事务持久化的操作状态。</param>
    /// <param name="values">本次审批人填写值。</param>
    public static void Merge(OperationState state, JsonElement? values)
    {
        if (values is null)
        {
            return;
        }

        if (values.Value.ValueKind != JsonValueKind.Object)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "审批人填写值必须是字段对象。");
        }

        foreach (var field in values.Value.EnumerateObject())
        {
            state.CurrentApproverFieldValues[field.Name] = field.Value.Clone();
        }
    }
}
