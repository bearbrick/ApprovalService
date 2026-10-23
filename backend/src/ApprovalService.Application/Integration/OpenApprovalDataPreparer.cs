using System.Text.Json;
using System.Text.Json.Nodes;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 发起前校验业务快照并在服务端计算公式，同时收集待原子关联的附件。
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
        IReadOnlyDictionary<string, object?>? context = null) =>
        PrepareCore(definition, data, context, allowAttachments: false).Data;

    /// <summary>
    /// 发起审批时保留已通过数据格式校验的附件 ID，供提交事务核对应用归属。
    /// </summary>
    /// <param name="definition">已发布流程的数据定义。</param>
    /// <param name="data">业务系统原始审批数据。</param>
    /// <param name="context">服务端生成的发起人上下文。</param>
    /// <returns>计算后的快照及按首次出现顺序去重的附件 ID。</returns>
    public static PreparedApprovalData PrepareWithAttachments(FlowDataDefinition definition, JsonElement data,
        IReadOnlyDictionary<string, object?>? context = null) =>
        PrepareCore(definition, data, context, allowAttachments: true);

    /// <summary>
    /// 共享字段校验和公式准备逻辑，只有发起事务允许携带附件。
    /// </summary>
    /// <param name="definition">已发布流程的数据定义。</param>
    /// <param name="data">业务系统原始审批数据。</param>
    /// <param name="context">服务端生成的发起人上下文。</param>
    /// <param name="allowAttachments">是否将附件交给后续原子归属校验。</param>
    /// <returns>计算后的快照及附件 ID。</returns>
    private static PreparedApprovalData PrepareCore(FlowDataDefinition definition, JsonElement data,
        IReadOnlyDictionary<string, object?>? context, bool allowAttachments)
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
        if (!allowAttachments)
        {
            RejectAttachments(definition.Fields, root, "data");
        }
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

        var attachmentIds = new List<string>();
        if (allowAttachments)
        {
            CollectAttachments(definition.Fields, root, attachmentIds, new HashSet<string>(StringComparer.Ordinal));
        }

        return new PreparedApprovalData
        {
            Data = JsonSerializer.SerializeToElement(root, ApprovalJson.Options),
            AttachmentIds = attachmentIds,
        };
    }

    /// <summary>
    /// 按发布字段递归读取顶层与明细附件，不把未知业务字段误当成文件引用。
    /// </summary>
    /// <param name="fields">当前层的字段定义。</param>
    /// <param name="container">当前层的业务数据。</param>
    /// <param name="ids">按首次出现顺序保留的附件 ID。</param>
    /// <param name="seen">跨字段去重集合。</param>
    private static void CollectAttachments(IEnumerable<FlowFieldDefinition> fields, JsonObject container,
        List<string> ids, HashSet<string> seen)
    {
        foreach (var field in fields)
        {
            if (field.Type == FlowFieldTypes.Attachment)
            {
                if (container[field.Key] is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        AddAttachment(item?.GetValue<string>(), ids, seen);
                    }
                }
                else if (container[field.Key] is JsonValue value)
                {
                    AddAttachment(value.GetValue<string>(), ids, seen);
                }
            }
            else if (field.Type == FlowFieldTypes.DetailTable && container[field.Key] is JsonArray rows)
            {
                foreach (var row in rows.OfType<JsonObject>())
                {
                    CollectAttachments(field.Children, row, ids, seen);
                }
            }
        }
    }

    /// <summary>
    /// 附件格式已由校验器确认，此处只做去重。
    /// </summary>
    /// <param name="id">本次引用的文件 ID。</param>
    /// <param name="ids">按首次出现顺序保留的附件 ID。</param>
    /// <param name="seen">跨字段去重集合。</param>
    private static void AddAttachment(string? id, List<string> ids, HashSet<string> seen)
    {
        if (id is not null && seen.Add(id))
        {
            ids.Add(id);
        }
    }

    /// <summary>
    /// 非发起用例没有附件关联事务，拒绝其提交附件 ID。
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

/// <summary>
/// 发起前准备好的业务快照和待由同一事务关联的附件 ID。
/// </summary>
public sealed class PreparedApprovalData
{
    /// <summary>
    /// 已校验并重算公式的审批数据。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }

    /// <summary>
    /// 按首次出现顺序去重的附件 ID。
    /// </summary>
    public IReadOnlyList<string> AttachmentIds { get; set; } = [];
}
