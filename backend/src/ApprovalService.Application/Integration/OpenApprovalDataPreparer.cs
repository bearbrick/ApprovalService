using System.Text.Json;
using System.Text.Json.Nodes;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 发起前校验业务快照并在服务端计算公式，避免业务系统篡改审批分支依据。
/// </summary>
public static class OpenApprovalDataPreparer
{
    /// <summary>
    /// 校验传入数据、拒绝尚未完成归属校验的附件，并生成可信计算字段。
    /// </summary>
    /// <param name="definition">已发布流程的数据定义。</param>
    /// <param name="data">业务系统原始审批数据。</param>
    /// <param name="context">服务端生成的发起人上下文。</param>
    /// <returns>可供条件求值和持久化的独立数据快照。</returns>
    public static JsonElement Prepare(FlowDataDefinition definition, JsonElement data,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = ApprovalDataValidator.Validate(definition, data);
        if (issues.Count > 0)
        {
            var first = issues[0];
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, $"{first.Field}：{first.Message}");
        }

        var root = JsonNode.Parse(data.GetRawText()) as JsonObject
            ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批数据必须是对象。");
        RejectAttachments(definition.Fields, root, "data");
        foreach (var field in definition.Fields)
        {
            if (field.ApproverInput || !string.IsNullOrWhiteSpace(field.Formula))
            {
                // 审批人填写值和计算结果不能取业务系统传来的值。
                root.Remove(field.Key);
            }

            if (field.Type == FlowFieldTypes.DetailTable && root[field.Key] is JsonArray rows)
            {
                PrepareRows(field, rows, root, context);
            }
        }

        var fields = definition.Fields.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var finished = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in definition.Fields.Where(item => !string.IsNullOrWhiteSpace(item.Formula)))
        {
            ComputeTopLevel(field, fields, root, context, visiting, finished);
        }

        return JsonSerializer.SerializeToElement(root, ApprovalJson.Options);
    }

    /// <summary>
    /// 在附件归属/关联事务接通前拒绝任何提交的附件 ID。
    /// </summary>
    /// <param name="fields">当前层的字段定义。</param>
    /// <param name="container">当前层的业务数据。</param>
    /// <param name="path">字段路径前缀。</param>
    private static void RejectAttachments(IEnumerable<FlowFieldDefinition> fields, JsonObject container, string path)
    {
        foreach (var field in fields)
        {
            if (field.Type == FlowFieldTypes.Attachment && container[field.Key] is not null)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"{path}.{field.Key}：附件归属校验尚未启用，暂不能提交附件 ID。");
            }

            if (field.Type == FlowFieldTypes.DetailTable && container[field.Key] is JsonArray rows)
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    if (rows[index] is JsonObject row)
                    {
                        RejectAttachments(field.Children, row, $"{path}.{field.Key}[{index}]");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 在每个明细行内按依赖顺序计算子字段公式。
    /// </summary>
    /// <param name="table">明细表定义。</param>
    /// <param name="rows">业务系统提交的行。</param>
    /// <param name="root">整个审批数据快照。</param>
    /// <param name="context">服务端上下文。</param>
    private static void PrepareRows(FlowFieldDefinition table, JsonArray rows, JsonObject root,
        IReadOnlyDictionary<string, object?>? context)
    {
        var children = table.Children.ToDictionary(item => item.Key, StringComparer.Ordinal);
        foreach (var rowNode in rows)
        {
            if (rowNode is not JsonObject row)
            {
                continue;
            }

            foreach (var child in table.Children.Where(item => item.ApproverInput || !string.IsNullOrWhiteSpace(item.Formula)))
            {
                row.Remove(child.Key);
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var finished = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in table.Children.Where(item => !string.IsNullOrWhiteSpace(item.Formula)))
            {
                ComputeRowField(table.Key, child, children, row, root, context, visiting, finished);
            }
        }
    }

    /// <summary>
    /// 递归计算顶层公式，并检测循环依赖。
    /// </summary>
    /// <param name="field">本次计算字段。</param>
    /// <param name="fields">全部顶层字段。</param>
    /// <param name="root">已复制的审批数据。</param>
    /// <param name="context">服务端上下文。</param>
    /// <param name="visiting">当前递归路径。</param>
    /// <param name="finished">已经计算完成的字段。</param>
    private static void ComputeTopLevel(FlowFieldDefinition field, IReadOnlyDictionary<string, FlowFieldDefinition> fields,
        JsonObject root, IReadOnlyDictionary<string, object?>? context, HashSet<string> visiting, HashSet<string> finished)
    {
        if (finished.Contains(field.Key))
        {
            return;
        }

        if (!visiting.Add(field.Key))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, $"计算字段 {field.Key} 存在循环依赖。");
        }

        foreach (var reference in ApprovalExpression.Validate(field.Formula!))
        {
            var rootKey = reference.Split('.')[0];
            if (fields.TryGetValue(rootKey, out var dependency) && !string.IsNullOrWhiteSpace(dependency.Formula))
            {
                ComputeTopLevel(dependency, fields, root, context, visiting, finished);
            }
        }

        root[field.Key] = Evaluate(field.Formula!, root, context, "data." + field.Key);
        visiting.Remove(field.Key);
        finished.Add(field.Key);
    }

    /// <summary>
    /// 递归计算明细行公式，引用当前表子字段时只读取当前行。
    /// </summary>
    /// <param name="tableKey">明细表字段 key。</param>
    /// <param name="field">本次计算子字段。</param>
    /// <param name="children">当前表全部子字段。</param>
    /// <param name="row">正在处理的行。</param>
    /// <param name="root">整个审批数据。</param>
    /// <param name="context">服务端上下文。</param>
    /// <param name="visiting">当前递归路径。</param>
    /// <param name="finished">本行已经计算完成的子字段。</param>
    private static void ComputeRowField(string tableKey, FlowFieldDefinition field,
        IReadOnlyDictionary<string, FlowFieldDefinition> children, JsonObject row, JsonObject root,
        IReadOnlyDictionary<string, object?>? context, HashSet<string> visiting, HashSet<string> finished)
    {
        if (finished.Contains(field.Key))
        {
            return;
        }

        if (!visiting.Add(field.Key))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                $"明细计算字段 {tableKey}.{field.Key} 存在循环依赖。");
        }

        foreach (var reference in ApprovalExpression.Validate(field.Formula!))
        {
            var prefix = tableKey + ".";
            if (reference.StartsWith(prefix, StringComparison.Ordinal) &&
                children.TryGetValue(reference[prefix.Length..], out var dependency) &&
                !string.IsNullOrWhiteSpace(dependency.Formula))
            {
                ComputeRowField(tableKey, dependency, children, row, root, context, visiting, finished);
            }
        }

        var view = (JsonObject)root.DeepClone();
        foreach (var (key, value) in row)
        {
            view[key] = value?.DeepClone();
        }

        // 字段定义里的表列路径在行计算时映射到当前行，其他顶层字段仍可读取。
        var rowFormula = field.Formula!.Replace("${" + tableKey + ".", "${", StringComparison.Ordinal);
        row[field.Key] = Evaluate(rowFormula, view, context, $"data.{tableKey}.{field.Key}");
        visiting.Remove(field.Key);
        finished.Add(field.Key);
    }

    /// <summary>
    /// 把可纠正的表达式求值错误定位到具体审批字段。
    /// </summary>
    /// <param name="formula">发布版本里的安全表达式。</param>
    /// <param name="data">本次可见的快照。</param>
    /// <param name="context">服务端上下文。</param>
    /// <param name="path">出错字段路径。</param>
    /// <returns>可赋给 JSON 快照的计算结果。</returns>
    private static JsonNode? Evaluate(string formula, JsonObject data, IReadOnlyDictionary<string, object?>? context,
        string path)
    {
        try
        {
            var value = ApprovalExpression.Evaluate(formula,
                JsonSerializer.SerializeToElement(data, ApprovalJson.Options), context);
            return JsonSerializer.SerializeToNode(value, ApprovalJson.Options);
        }
        catch (ApprovalExpressionException error)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, $"{path}：计算失败，{error.Message}");
        }
    }
}
