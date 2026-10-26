using System.Text.Json;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Definitions;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Application.Runtime;

/// <summary>
/// 按当前业务快照和已提交的审批人字段计算执行图条件，尚未填写的审批人字段留待网关到达时校验。
/// </summary>
internal static class FlowConditionEvaluator
{
    /// <summary>
    /// 为本次命令构造条件快照；本次字段只由成功推进的任务命令传入。
    /// </summary>
    /// <param name="definition">锁定的数据字段定义。</param>
    /// <param name="graph">锁定的执行图。</param>
    /// <param name="businessData">当前业务数据。</param>
    /// <param name="currentValues">已成功提交的审批人字段。</param>
    /// <param name="pendingValues">本次成功推进准备提交的审批人字段。</param>
    /// <param name="context">可信发起部门和职级等表达式上下文。</param>
    /// <returns>已能求值的连线条件；未填写的审批人字段不预先决定路径。</returns>
    public static Dictionary<string, bool> Evaluate(FlowDataDefinition definition, FlowExecutionGraph graph,
        JsonElement businessData, IReadOnlyDictionary<string, JsonElement>? currentValues = null,
        JsonElement? pendingValues = null, IReadOnlyDictionary<string, object?>? context = null)
    {
        var approverKeys = definition.Fields.Where(field => field.ApproverInput)
            .Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in businessData.EnumerateObject())
        {
            // 业务发起数据绝不能伪造审批人字段，即使历史快照里混入了同名键。
            if (!approverKeys.Contains(field.Name))
            {
                values[field.Name] = field.Value.Clone();
            }
        }

        if (currentValues is not null)
        {
            foreach (var field in currentValues.Where(field => approverKeys.Contains(field.Key)))
            {
                values[field.Key] = field.Value.Clone();
            }
        }

        if (pendingValues?.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in pendingValues.Value.EnumerateObject().Where(field =>
                approverKeys.Contains(field.Name)))
            {
                values[field.Name] = field.Value.Clone();
            }
        }

        var data = JsonSerializer.SerializeToElement(values);
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(edge => !edge.IsDefault &&
            !string.IsNullOrWhiteSpace(edge.Condition)))
        {
            try
            {
                var references = ApprovalExpression.Validate(edge.Condition!);
                // 到达该字段的填写节点前无法求值；实际到达网关时缺值会被引擎拒绝。
                if (references.Any(reference => approverKeys.Contains(reference.Split('.')[0]) &&
                    !values.ContainsKey(reference.Split('.')[0])))
                {
                    continue;
                }

                var evaluated = ApprovalExpression.Evaluate(edge.Condition!, data, context);
                result[edge.Id] = evaluated is bool selected ? selected :
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                        $"条件 {edge.Id} 的结果必须为真假值。");
            }
            catch (ApprovalExpressionException error)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"条件 {edge.Id} 计算失败：{error.Message}");
            }
        }

        return result;
    }
}
