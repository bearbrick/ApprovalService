using System.Globalization;
using System.Text.Json;
using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Expressions;

/// <summary>
/// 审批数据的一项逐字段错误。
/// </summary>
public sealed class ApprovalDataIssue
{
    /// <summary>
    /// JSON 路径，从 data 开始。
    /// </summary>
    public required string Field
    {
        get; init;
    }

    /// <summary>
    /// 中文错误说明。
    /// </summary>
    public required string Message
    {
        get; init;
    }
}

/// <summary>
/// 按流程数据定义校验业务系统提交的快照，并生成样例数据。
/// </summary>
public static class ApprovalDataValidator
{
    /// <summary>
    /// 单张审批单允许的最多明细行数。
    /// </summary>
    public const int MaxDetailRows = 1000;

    /// <summary>
    /// 校验提交快照；计算字段和审批人填写字段不要求业务系统提交。
    /// </summary>
    /// <param name="definition">流程数据定义。</param>
    /// <param name="data">业务系统提交的 JSON 对象。</param>
    /// <returns>全部逐字段错误；空集合表示通过。</returns>
    public static IReadOnlyList<ApprovalDataIssue> Validate(FlowDataDefinition definition, JsonElement data)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<ApprovalDataIssue>();
        if (data.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new ApprovalDataIssue { Field = "data", Message = "审批数据必须是对象。" });
            return issues;
        }

        foreach (var field in definition.Fields)
        {
            ValidateField(field, data, "data", issues);
        }

        return issues;
    }

    /// <summary>
    /// 为同一份定义生成可序列化的业务系统提交样例。
    /// </summary>
    /// <param name="definition">流程数据定义。</param>
    /// <returns>小驼峰字段 key 的 JSON 样例。</returns>
    public static string CreateExampleJson(FlowDataDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var sample = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in definition.Fields.Where(item => !item.ApproverInput && string.IsNullOrWhiteSpace(item.Formula)))
        {
            sample[field.Key] = Sample(field);
        }

        return JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 校验一个字段和明细行；路径包含实际行号。
    /// </summary>
    /// <param name="field">字段定义。</param>
    /// <param name="container">所在对象。</param>
    /// <param name="prefix">路径前缀。</param>
    /// <param name="issues">错误集合。</param>
    private static void ValidateField(FlowFieldDefinition field, JsonElement container, string prefix, List<ApprovalDataIssue> issues)
    {
        var path = prefix + "." + field.Key;
        if (field.ApproverInput || !string.IsNullOrWhiteSpace(field.Formula))
        {
            // 审批人字段在节点操作时填写；计算字段由引擎求值。
            return;
        }

        if (!container.TryGetProperty(field.Key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (field.Required)
            {
                issues.Add(new ApprovalDataIssue { Field = path, Message = $"{field.Name}为必传字段。" });
            }

            return;
        }

        if (field.Type == FlowFieldTypes.DetailTable)
        {
            ValidateTable(field, value, path, issues);
            return;
        }

        if (field.Type == FlowFieldTypes.Attachment && value.ValueKind == JsonValueKind.Array)
        {
            ValidateAttachments(field, value, path, issues);
            return;
        }

        if (!IsValidValue(field.Type, value) || field.Required && IsEmptyValue(field.Type, value))
        {
            issues.Add(new ApprovalDataIssue { Field = path, Message = $"{field.Name}的类型或格式不正确。" });
        }
    }

    /// <summary>
    /// 逐项检查附件数组中的文件 ID，返回带下标的错误路径。
    /// </summary>
    /// <param name="field">附件字段定义。</param>
    /// <param name="value">附件数组。</param>
    /// <param name="path">字段路径。</param>
    /// <param name="issues">错误集合。</param>
    private static void ValidateAttachments(FlowFieldDefinition field, JsonElement value, string path, List<ApprovalDataIssue> issues)
    {
        if (field.Required && value.GetArrayLength() == 0)
        {
            issues.Add(new ApprovalDataIssue { Field = path, Message = $"{field.Name}至少需要一个文件。" });
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                issues.Add(new ApprovalDataIssue { Field = $"{path}[{index}]", Message = "附件文件 ID 不能为空。" });
            }

            index++;
        }
    }

    /// <summary>
    /// 校验明细表数组和每一行。
    /// </summary>
    /// <param name="field">明细字段。</param>
    /// <param name="value">明细数据。</param>
    /// <param name="path">明细路径。</param>
    /// <param name="issues">错误集合。</param>
    private static void ValidateTable(FlowFieldDefinition field, JsonElement value, string path, List<ApprovalDataIssue> issues)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new ApprovalDataIssue { Field = path, Message = $"{field.Name}必须是明细数组。" });
            return;
        }

        var count = value.GetArrayLength();
        if (count > MaxDetailRows)
        {
            issues.Add(new ApprovalDataIssue { Field = path, Message = $"{field.Name}的明细行数超过上限。" });
            return;
        }

        if (field.Required && count == 0)
        {
            issues.Add(new ApprovalDataIssue { Field = path, Message = $"{field.Name}至少需要一行。" });
        }

        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var rowPath = $"{path}[{index++}]";
            if (row.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new ApprovalDataIssue { Field = rowPath, Message = "明细行必须是对象。" });
                continue;
            }

            foreach (var child in field.Children)
            {
                ValidateField(child, row, rowPath, issues);
            }
        }
    }

    /// <summary>
    /// 检查普通字段的 JSON 类型和日期格式。
    /// </summary>
    /// <param name="type">定义中的字段类型。</param>
    /// <param name="value">JSON 值。</param>
    /// <returns>值是否符合类型。</returns>
    private static bool IsValidValue(string type, JsonElement value)
    {
        return type switch
        {
            FlowFieldTypes.Text or FlowFieldTypes.Person or FlowFieldTypes.Department => value.ValueKind == JsonValueKind.String,
            FlowFieldTypes.Number or FlowFieldTypes.Money => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            FlowFieldTypes.Date => value.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            FlowFieldTypes.Attachment => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                || value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(item.GetString())),
            _ => false,
        };
    }

    /// <summary>
    /// 检查必传字段是否为空值。
    /// </summary>
    /// <param name="type">定义中的字段类型。</param>
    /// <param name="value">JSON 值。</param>
    /// <returns>是否为空。</returns>
    private static bool IsEmptyValue(string type, JsonElement value)
    {
        return type switch
        {
            FlowFieldTypes.Text or FlowFieldTypes.Person or FlowFieldTypes.Department => string.IsNullOrWhiteSpace(value.GetString()),
            FlowFieldTypes.Attachment when value.ValueKind == JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
            FlowFieldTypes.Attachment when value.ValueKind == JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false,
        };
    }

    /// <summary>
    /// 为一个字段生成最小演示值。
    /// </summary>
    /// <param name="field">字段定义。</param>
    /// <returns>可序列化的演示值。</returns>
    private static object? Sample(FlowFieldDefinition field)
    {
        return field.Type switch
        {
            FlowFieldTypes.Text => "示例文本",
            FlowFieldTypes.Number => 1,
            FlowFieldTypes.Money => 100.00m,
            FlowFieldTypes.Date => "2026-01-01",
            FlowFieldTypes.Person => "EMP001",
            FlowFieldTypes.Department => "DEP001",
            FlowFieldTypes.Attachment => field.Required ? new[] { "FILE_EXAMPLE" } : Array.Empty<string>(),
            FlowFieldTypes.DetailTable => new[]
            {
                field.Children.Where(child => !child.ApproverInput && string.IsNullOrWhiteSpace(child.Formula))
                    .ToDictionary(child => child.Key, Sample, StringComparer.Ordinal),
            },
            _ => null,
        };
    }
}
