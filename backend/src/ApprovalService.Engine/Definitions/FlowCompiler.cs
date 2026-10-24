using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Definitions;

/// <summary>
/// 将已校验的自由画布编译为不含设计坐标的执行图。
/// </summary>
public static class FlowCompiler
{
    /// <summary>
    /// 一次报告全部发布问题；存在问题时不产生执行图。
    /// </summary>
    /// <param name="definition">待发布的流程定义。</param>
    /// <returns>执行图或全部中文发布问题。</returns>
    public static FlowCompilationResult Compile(FlowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var result = new FlowCompilationResult();
        if (definition.Canvas is null)
        {
            result.Issues.Add(new FlowValidationIssue { Message = "流程缺少自由画布。" });
        }
        else
        {
            FlowGraphValidator.Validate(definition.Canvas, result.Issues);
        }

        FlowSemanticValidator.Validate(definition, result.Issues);
        if (result.Issues.Count > 0 || definition.Canvas is null)
        {
            return result;
        }

        // 执行图只保留运行语义；嵌套配置深拷贝，发布后修改画布也不会改变已编译版本。
        result.Graph = new FlowExecutionGraph
        {
            SchemaVersion = definition.SchemaVersion,
            WithdrawBeforeFirstDecision = definition.WithdrawBeforeFirstDecision,
            ReturnMode = definition.ReturnMode,
            IsConfidential = definition.IsConfidential,
            Nodes = definition.Canvas.Nodes.Select(node => new FlowExecutionNode
            {
                Id = node.Id,
                Type = node.Type,
                Name = node.Name,
                GatewayPairId = node.GatewayPairId,
                GatewayDirection = node.GatewayDirection,
                Assignee = Clone(node.Assignee),
                Approval = Clone(node.Approval),
                Subprocess = Clone(node.Subprocess),
                Automation = Clone(node.Automation),
                FieldPermissions = Clone(node.FieldPermissions) ?? [],
            }).ToList(),
            Edges = definition.Canvas.Edges.Select(edge => new FlowExecutionEdge
            {
                Id = edge.Id,
                SourceId = edge.SourceId,
                TargetId = edge.TargetId,
                Priority = edge.Priority,
                IsDefault = edge.IsDefault,
                Condition = edge.Condition,
            }).ToList(),
        };
        return result;
    }

    /// <summary>
    /// 复制嵌套配置，避免设计版和执行版共享可变对象。
    /// </summary>
    /// <typeparam name="T">配置类型。</typeparam>
    /// <param name="value">原始配置。</param>
    /// <returns>独立副本；原值为空时也为空。</returns>
    private static T? Clone<T>(T? value)
    {
        if (value is null)
        {
            return default;
        }

        var json = JsonSerializer.Serialize(value, ApprovalJson.Options);
        return JsonSerializer.Deserialize<T>(json, ApprovalJson.Options);
    }
}
