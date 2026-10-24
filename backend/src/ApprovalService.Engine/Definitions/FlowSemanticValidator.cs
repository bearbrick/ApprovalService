using System.Text.Json;
using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Engine.Definitions;

/// <summary>
/// 检查数据字段、条件表达式和人工节点的发布配置。
/// </summary>
internal static class FlowSemanticValidator
{
    /// <summary>
    /// 对流程定义中与数据、人员有关的配置作静态检查。
    /// </summary>
    /// <param name="definition">待发布定义。</param>
    /// <param name="issues">收集全部问题。</param>
    public static void Validate(FlowDefinition definition, List<FlowValidationIssue> issues)
    {
        if (definition.SchemaVersion != FlowSchema.CurrentVersion)
        {
            Add(issues, null, $"流程定义格式版本不受支持：{definition.SchemaVersion}。");
        }

        if (string.IsNullOrWhiteSpace(definition.Code))
        {
            Add(issues, null, "流程编码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            Add(issues, null, "流程名称不能为空。");
        }

        if (definition.ReturnMode is not (FlowReturnModes.Restart or FlowReturnModes.ReturnNode))
        {
            Add(issues, null, "退回后重新提交方式只能是 restart 或 returnNode。");
        }

        var fields = CollectFields(definition.Data?.Fields ?? [], issues);
        foreach (var edge in definition.Canvas?.Edges ?? [])
        {
            CheckReferences(edge.Condition, edge.Id, fields, issues);
        }

        foreach (var field in EnumerateFields(definition.Data?.Fields ?? []))
        {
            CheckReferences(field.Formula, field.Key, fields, issues);
        }

        foreach (var node in definition.Canvas?.Nodes ?? [])
        {
            if (node.Type is FlowNodeTypes.Approval or FlowNodeTypes.Handling or FlowNodeTypes.Cc &&
                string.IsNullOrWhiteSpace(node.Assignee?.Source))
            {
                Add(issues, node.Id, $"人工节点“{node.Id}”必须配置处理人来源。");
            }

            if (!string.IsNullOrWhiteSpace(node.Assignee?.FieldKey) && !fields.Contains(node.Assignee.FieldKey))
            {
                Add(issues, node.Id, $"节点“{node.Id}”的处理人字段“{node.Assignee.FieldKey}”不存在。");
            }

            // 指定人员兜底是静态必填配置；实际人员状态仍在到达节点时核对。
            if (node.Type is FlowNodeTypes.Approval or FlowNodeTypes.Handling or FlowNodeTypes.Cc &&
                node.Approval?.MissingAssigneeBehavior == FlowMissingAssigneeBehaviors.Specified &&
                string.IsNullOrWhiteSpace(node.Approval.MissingAssigneeUserId))
            {
                Add(issues, node.Id, $"节点“{node.Id}”选择找不到处理人时转交指定人员，必须填写指定人员 ID。");
            }

            foreach (var permission in node.FieldPermissions ?? [])
            {
                if (!fields.Contains(permission.FieldKey))
                {
                    Add(issues, node.Id, $"节点“{node.Id}”的字段权限引用了不存在的字段“{permission.FieldKey}”。");
                }
            }

            foreach (var duplicate in (node.FieldPermissions ?? []).GroupBy(item => item.FieldKey,
                StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                Add(issues, node.Id, $"节点“{node.Id}”的字段“{duplicate.Key}”配置了重复权限。");
            }
        }

        foreach (var summaryKey in definition.Data?.SummaryFields ?? [])
        {
            if (!fields.Contains(summaryKey))
            {
                Add(issues, summaryKey, $"摘要字段“{summaryKey}”不存在。");
            }
        }
    }

    /// <summary>
    /// 收集业务字段的完整路径，包括明细表的子字段，并报告重复或无效定义。
    /// </summary>
    /// <param name="roots">顶层字段。</param>
    /// <param name="issues">收集全部问题。</param>
    /// <returns>可供条件引用的字段路径。</returns>
    private static HashSet<string> CollectFields(IEnumerable<FlowFieldDefinition> roots, List<FlowValidationIssue> issues)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        Visit(roots, string.Empty);
        return keys;

        void Visit(IEnumerable<FlowFieldDefinition> level, string prefix)
        {
            foreach (var field in level)
            {
                if (string.IsNullOrWhiteSpace(field.Key))
                {
                    Add(issues, prefix.Length == 0 ? null : prefix, "审批数据字段缺少 key。");
                    continue;
                }

                var path = prefix.Length == 0 ? field.Key : prefix + "." + field.Key;
                if (!keys.Add(path))
                {
                    Add(issues, path, $"审批数据字段“{path}”重复。");
                }

                if (!IsKnownFieldType(field.Type))
                {
                    Add(issues, path, $"审批数据字段“{path}”的类型不受支持。");
                }

                if (field.Type != FlowFieldTypes.DetailTable && (field.Children?.Count ?? 0) > 0)
                {
                    Add(issues, path, $"非明细表字段“{path}”不能包含子字段。");
                }

                if (field.Type == FlowFieldTypes.DetailTable && (field.Children?.Count ?? 0) == 0)
                {
                    Add(issues, path, $"明细表字段“{path}”至少要有一个子字段。");
                }

                Visit(field.Children ?? [], path);
            }
        }
    }

    /// <summary>
    /// 依定义顺序列出顶层和子字段，用于逐个检查计算表达式。
    /// </summary>
    /// <param name="roots">顶层字段。</param>
    /// <returns>所有字段。</returns>
    private static IEnumerable<FlowFieldDefinition> EnumerateFields(IEnumerable<FlowFieldDefinition> roots)
    {
        foreach (var field in roots)
        {
            yield return field;
            foreach (var child in EnumerateFields(field.Children ?? []))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// 解析受限表达式并检查显式字段引用；常量表达式也检查可否求值。
    /// </summary>
    /// <param name="expression">条件或计算表达式。</param>
    /// <param name="elementId">连线或字段定位。</param>
    /// <param name="fields">已定义的字段路径。</param>
    /// <param name="issues">收集全部问题。</param>
    private static void CheckReferences(string? expression, string? elementId, HashSet<string> fields, List<FlowValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        IReadOnlyList<string> references;
        try
        {
            references = ApprovalExpression.Validate(expression);
            if (references.Count == 0)
            {
                // 无字段引用的常量表达式可在发布阶段确定，及早拒绝除零等错误。
                using var emptyData = JsonDocument.Parse("{}");
                ApprovalExpression.Evaluate(expression, emptyData.RootElement);
            }
        }
        catch (ApprovalExpressionException error)
        {
            Add(issues, elementId, $"表达式“{elementId}”无效：{error.Message}");
            return;
        }

        foreach (var key in references)
        {
            if (!fields.Contains(key) && key is not ("initiator.department" or "initiator.role" or "initiator.level"))
            {
                Add(issues, elementId, $"表达式“{elementId}”引用了不存在的字段“{key}”。");
            }
        }
    }

    /// <summary>
    /// 判断字段类型是否属于当前契约支持的类型。
    /// </summary>
    /// <param name="type">字段类型。</param>
    /// <returns>支持时为真。</returns>
    private static bool IsKnownFieldType(string type) => type is FlowFieldTypes.Text or FlowFieldTypes.Number or
        FlowFieldTypes.Money or FlowFieldTypes.Date or FlowFieldTypes.Person or FlowFieldTypes.Department or
        FlowFieldTypes.Attachment or FlowFieldTypes.DetailTable;

    /// <summary>
    /// 加入一条中文发布问题。
    /// </summary>
    /// <param name="issues">问题集合。</param>
    /// <param name="elementId">相关元素。</param>
    /// <param name="message">问题说明。</param>
    private static void Add(List<FlowValidationIssue> issues, string? elementId, string message) =>
        issues.Add(new FlowValidationIssue { ElementId = elementId, Message = message });
}
