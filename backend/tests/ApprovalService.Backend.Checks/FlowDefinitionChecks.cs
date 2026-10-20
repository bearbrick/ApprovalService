using System.Text.Json;
using System.Xml.Linq;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Definitions;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 正式自由画布流程定义的场景检查：样例、发布校验、布局隔离和 BPMN 往返。
/// </summary>
internal static class FlowDefinitionChecks
{
    /// <summary>
    /// 运行流程定义与发布校验场景。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <param name="check">记录检查结果。</param>
    public static void Run(string backendRoot, Action<string, bool> check)
    {
        var samplesDirectory = Path.Combine(backendRoot, "samples", "flows");
        var files = Directory.GetFiles(samplesDirectory, "*.json").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        check("采购、报销和质量异常有三份正式流程样例", files.Length == 3);

        var flows = files.Select(ReadFlow).ToArray();
        check("三个样例都能发布编译，且每份都有开始和结束节点",
            flows.All(flow =>
            {
                var result = FlowCompiler.Compile(flow);
                return result.Graph is not null && result.Issues.Count == 0
                    && flow.Canvas.Nodes.Any(node => node.Type == FlowNodeTypes.Start)
                    && flow.Canvas.Nodes.Any(node => node.Type == FlowNodeTypes.End);
            }));

        var nodeTypes = flows.SelectMany(flow => flow.Canvas.Nodes).Select(node => node.Type).ToHashSet(StringComparer.Ordinal);
        check("样例覆盖审批、办理、抄送、三种网关、子流程和自动节点",
            new[]
            {
                FlowNodeTypes.Approval, FlowNodeTypes.Handling, FlowNodeTypes.Cc,
                FlowNodeTypes.ExclusiveGateway, FlowNodeTypes.InclusiveGateway,
                FlowNodeTypes.ParallelGateway, FlowNodeTypes.Subprocess, FlowNodeTypes.Automatic,
            }.All(nodeTypes.Contains));
        check("正式样例覆盖明细、计算、摘要、审批人填写、字段权限、详情排版和打印设置",
            flows.Any(flow => flow.Data.Fields.Any(field => field.Type == FlowFieldTypes.DetailTable && field.Children.Count > 0))
            && flows.Any(flow => flow.Data.Fields.Any(field => !string.IsNullOrWhiteSpace(field.Formula)))
            && flows.Any(flow => flow.Data.Fields.Any(field => field.ApproverInput))
            && flows.All(flow => flow.Data.SummaryFields.Count > 0 && flow.Canvas.Nodes.Any(node => node.FieldPermissions.Count > 0))
            && flows.Any(flow => flow.DetailLayout.Mode == "custom" && flow.DetailLayout.Groups.Count > 0)
            && flows.Any(flow => flow.PrintSettings.IncludeReceiptPages));

        var layout = Copy(flows[0]);
        var originalGraph = FlowCompiler.Compile(layout).Graph;
        foreach (var node in layout.Canvas.Nodes)
        {
            node.X += 731;
            node.Y -= 219;
        }

        check("移动画布节点不改变执行图",
            originalGraph is not null && JsonSerializer.Serialize(originalGraph, ApprovalJson.Options)
            == JsonSerializer.Serialize(FlowCompiler.Compile(layout).Graph, ApprovalJson.Options));

        CheckOperationPolicies(flows, check);
        CheckInvalidDefinitions(flows, check);
        CheckBpmn(flows, check);
    }

    /// <summary>
    /// 检查旧定义的缺省策略、新策略的发布固定值和非法模式。
    /// </summary>
    /// <param name="flows">未显式写策略的正式样例。</param>
    /// <param name="check">记录检查结果。</param>
    private static void CheckOperationPolicies(IReadOnlyList<FlowDefinition> flows, Action<string, bool> check)
    {
        check("旧流程定义省略策略时仍可在结束前撤回并从头重提",
            flows.All(flow => !flow.WithdrawBeforeFirstDecision && flow.ReturnMode == FlowReturnModes.Restart
                && FlowCompiler.Compile(flow).Graph is { WithdrawBeforeFirstDecision: false, ReturnMode: FlowReturnModes.Restart }));

        var configured = Copy(flows[0]);
        configured.WithdrawBeforeFirstDecision = true;
        configured.ReturnMode = FlowReturnModes.ReturnNode;
        var graph = FlowCompiler.Compile(configured).Graph;
        check("流程级限制编译入发布图且不受草稿后续修改影响",
            graph is { WithdrawBeforeFirstDecision: true, ReturnMode: FlowReturnModes.ReturnNode });
        configured.WithdrawBeforeFirstDecision = false;
        configured.ReturnMode = FlowReturnModes.Restart;
        check("发布图保留原撤回和重提策略",
            graph is { WithdrawBeforeFirstDecision: true, ReturnMode: FlowReturnModes.ReturnNode });

        configured.ReturnMode = "unknown";
        check("不支持的退回重提方式拒绝发布并给中文问题",
            FlowCompiler.Compile(configured).Issues.Any(issue => issue.Message.Contains("退回后重新提交方式", StringComparison.Ordinal)));

        configured.ReturnMode = FlowReturnModes.ReturnNode;
        configured.WithdrawBeforeFirstDecision = true;
        var imported = BpmnFlowCodec.Import(BpmnFlowCodec.Export(configured));
        check("BPMN 往返保留流程级撤回和重提策略",
            imported.WithdrawBeforeFirstDecision && imported.ReturnMode == FlowReturnModes.ReturnNode
            && FlowCompiler.Compile(imported).Graph is { WithdrawBeforeFirstDecision: true, ReturnMode: FlowReturnModes.ReturnNode });
    }

    /// <summary>
    /// 检查无效自由画布会列出全部定位问题，不因第一处错误停下。
    /// </summary>
    /// <param name="flows">三份有效样例。</param>
    /// <param name="check">记录检查结果。</param>
    private static void CheckInvalidDefinitions(IReadOnlyList<FlowDefinition> flows, Action<string, bool> check)
    {
        var baseline = flows.First(flow => flow.Canvas.Nodes.Any(node => node.Type == FlowNodeTypes.ExclusiveGateway));
        var invalid = Copy(baseline);
        var approval = invalid.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.Approval);
        approval.Assignee = null;

        var gateway = invalid.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.ExclusiveGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        var conditional = invalid.Canvas.Edges.First(edge => edge.SourceId == gateway.Id && !edge.IsDefault);
        conditional.Condition = null;

        var orphan = Copy(baseline).Canvas.Nodes.First(node => node.Type == FlowNodeTypes.Approval);
        orphan.Id = "unreachable-approval";
        invalid.Canvas.Nodes.Add(orphan);
        var result = FlowCompiler.Compile(invalid);
        check("缺审批人、缺分支条件和孤立节点一次返回全部中文定位问题",
            result.Graph is null && result.Issues.Count >= 3
            && result.Issues.Any(issue => issue.ElementId == approval.Id)
            && result.Issues.Any(issue => issue.ElementId == conditional.Id)
            && result.Issues.Any(issue => issue.ElementId == orphan.Id)
            && result.Issues.All(issue => issue.Message.Length > 0));

        var missingField = Copy(baseline);
        var missingGateway = missingField.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.ExclusiveGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        var missingEdge = missingField.Canvas.Edges.First(edge => edge.SourceId == missingGateway.Id && !edge.IsDefault);
        missingEdge.Condition = "${unknownField} > 0";
        check("分支条件引用不存在的审批字段时拒绝发布并定位连线",
            FlowCompiler.Compile(missingField).Issues.Any(issue => issue.ElementId == missingEdge.Id));

        var invalidExpression = Copy(baseline);
        var expressionGateway = invalidExpression.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.ExclusiveGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        var expressionEdge = invalidExpression.Canvas.Edges.First(edge => edge.SourceId == expressionGateway.Id && !edge.IsDefault);
        expressionEdge.Condition = "EXEC('bad')";
        check("未知函数条件在发布时拒绝并定位连线",
            FlowCompiler.Compile(invalidExpression).Issues.Any(issue => issue.ElementId == expressionEdge.Id && issue.Message.Contains("不支持函数", StringComparison.Ordinal)));
        expressionEdge.Condition = "1 / 0";
        check("常量条件除零在发布时拒绝并定位连线",
            FlowCompiler.Compile(invalidExpression).Issues.Any(issue => issue.ElementId == expressionEdge.Id && issue.Message.Contains("为零", StringComparison.Ordinal)));

        var nestedField = Copy(flows.First(flow => flow.Data.Fields.Any(field => field.Key == "items")));
        var nestedGateway = nestedField.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.ExclusiveGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        var nestedEdge = nestedField.Canvas.Edges.First(edge => edge.SourceId == nestedGateway.Id && !edge.IsDefault);
        nestedEdge.Condition = "${items.quantity} > 0 && ${initiator.department} == \"PURCHASE\"";
        check("条件可引用明细子字段和预留的发起人部门上下文",
            FlowCompiler.Compile(nestedField).Graph is not null);

        var noDefault = Copy(baseline);
        var defaultGateway = noDefault.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.ExclusiveGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        foreach (var edge in noDefault.Canvas.Edges.Where(edge => edge.SourceId == defaultGateway.Id))
        {
            edge.IsDefault = false;
        }

        check("排他分支缺少兜底连线时拒绝发布并定位网关",
            FlowCompiler.Compile(noDefault).Issues.Any(issue => issue.ElementId == defaultGateway.Id));

        var repeated = Copy(baseline);
        repeated.Canvas.Nodes.Add(Copy(baseline).Canvas.Nodes.First(node => node.Type == FlowNodeTypes.Approval));
        check("重复节点 ID 与不兼容的格式版本都被拒绝",
            FlowCompiler.Compile(repeated).Issues.Any(issue => issue.Message.Contains("重复", StringComparison.Ordinal))
            && FlowCompiler.Compile(new FlowDefinition { SchemaVersion = FlowSchema.CurrentVersion + 1 }).Issues
                .Any(issue => issue.Message.Contains("版本", StringComparison.Ordinal)));

        var parallel = flows.First(flow => flow.Canvas.Nodes.Any(node => node.Type == FlowNodeTypes.ParallelGateway));
        var unpaired = Copy(parallel);
        var split = unpaired.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.ParallelGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        split.GatewayPairId = "missing-pair";
        check("并行网关分叉与汇合未配对时拒绝发布",
            FlowCompiler.Compile(unpaired).Issues.Any(issue => issue.ElementId == split.Id));

        var bypass = Copy(parallel);
        var bypassSplit = bypass.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.ParallelGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        var branch = bypass.Canvas.Edges.First(edge => edge.SourceId == bypassSplit.Id);
        var branchExit = bypass.Canvas.Edges.First(edge => edge.SourceId == branch.TargetId);
        branchExit.TargetId = bypass.Canvas.Nodes.Single(node => node.Type == FlowNodeTypes.End).Id;
        check("并行分支绕过汇合直接结束时拒绝发布",
            FlowCompiler.Compile(bypass).Issues.Any(issue => issue.Message.Contains("绕过汇合", StringComparison.Ordinal)));

        var inclusive = Copy(flows.First(flow => flow.Canvas.Nodes.Any(node => node.Type == FlowNodeTypes.InclusiveGateway)));
        var inclusiveSplit = inclusive.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.InclusiveGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        inclusiveSplit.GatewayPairId = null;
        check("包容分支缺少汇合配对时拒绝发布",
            FlowCompiler.Compile(inclusive).Issues.Any(issue => issue.ElementId == inclusiveSplit.Id));

        var inclusiveCondition = Copy(flows.First(flow => flow.Canvas.Nodes.Any(node => node.Type == FlowNodeTypes.InclusiveGateway)));
        var conditionSplit = inclusiveCondition.Canvas.Nodes.First(node => node.Type == FlowNodeTypes.InclusiveGateway && node.GatewayDirection == FlowGatewayDirections.Split);
        var conditionEdge = inclusiveCondition.Canvas.Edges.First(edge => edge.SourceId == conditionSplit.Id && !edge.IsDefault);
        conditionEdge.Condition = null;
        check("包容分支的非兜底连线也必须设置条件",
            FlowCompiler.Compile(inclusiveCondition).Issues.Any(issue => issue.ElementId == conditionEdge.Id));

        var disconnected = Copy(baseline);
        var disconnectedEdge = disconnected.Canvas.Edges.First();
        disconnectedEdge.TargetId = "missing-target";
        check("连线端点不存在时报告连线与失去路径的节点",
            FlowCompiler.Compile(disconnected).Issues.Any(issue => issue.ElementId == disconnectedEdge.Id));
    }

    /// <summary>
    /// 检查 BPMN 是真实图形文件、完整往返，并拒绝外部或损坏的导出。
    /// </summary>
    /// <param name="flows">三份有效样例。</param>
    /// <param name="check">记录检查结果。</param>
    private static void CheckBpmn(IReadOnlyList<FlowDefinition> flows, Action<string, bool> check)
    {
        check("三个正式流程均可导出 BPMN 节点及连线并无损导入",
            flows.All(flow =>
            {
                var xml = BpmnFlowCodec.Export(flow);
                var imported = BpmnFlowCodec.Import(xml);
                return xml.Contains("sequenceFlow", StringComparison.Ordinal)
                    && xml.Contains("extensionElements", StringComparison.Ordinal)
                    && JsonSerializer.Serialize(flow, ApprovalJson.Options) == JsonSerializer.Serialize(imported, ApprovalJson.Options)
                    && FlowCompiler.Compile(imported).Graph is not null;
            }));

        var exported = BpmnFlowCodec.Export(flows[0]);
        check("没有本系统扩展的外部 BPMN 文件被明确拒绝",
            RejectsFormat("<definitions xmlns=\"http://www.omg.org/spec/BPMN/20100524/MODEL\"><process id=\"outside\" /></definitions>"));
        check("本系统导出文件的来源标记损坏时拒绝导入",
            RejectsFormat(exported.Replace("producer=\"ApprovalService\"", "producer=\"Other\"", StringComparison.Ordinal)));

        var tampered = XDocument.Parse(exported);
        tampered.Descendants().First(element => element.Name.LocalName == "sequenceFlow").SetAttributeValue("sourceRef", "OtherNode");
        check("即使扩展 JSON 未改，标准 BPMN 图被篡改也拒绝导入", RejectsFormat(tampered.ToString()));
    }

    /// <summary>
    /// 读取一份正式流程 JSON 样例。
    /// </summary>
    /// <param name="path">样例文件路径。</param>
    /// <returns>流程定义。</returns>
    private static FlowDefinition ReadFlow(string path) =>
        JsonSerializer.Deserialize<FlowDefinition>(File.ReadAllText(path), ApprovalJson.Options)
        ?? throw new InvalidDataException($"流程样例 {Path.GetFileName(path)} 为空。");

    /// <summary>
    /// 通过正式 JSON 写法复制流程，确保检查不会改动共享的样例对象。
    /// </summary>
    /// <param name="flow">原流程。</param>
    /// <returns>独立的流程副本。</returns>
    private static FlowDefinition Copy(FlowDefinition flow) =>
        JsonSerializer.Deserialize<FlowDefinition>(JsonSerializer.Serialize(flow, ApprovalJson.Options), ApprovalJson.Options)!;

    /// <summary>
    /// 判断导入文件是否因来源或格式不合规而给出明确异常。
    /// </summary>
    /// <param name="xml">待导入文件内容。</param>
    /// <returns>抛出带中文消息的格式异常时为 true。</returns>
    private static bool RejectsFormat(string xml)
    {
        try
        {
            BpmnFlowCodec.Import(xml);
            return false;
        }
        catch (FormatException exception)
        {
            return exception.Message.Length > 0;
        }
    }
}
