using System.Text.Json;
using System.Text.RegularExpressions;
using ApprovalService.Contracts.Flows;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 契约一致性（AGENTS.md §6.4）：HTTP 头、错误码只在后端 Contracts 定义，调用端 SDK 的副本和 docs/contracts.md 要跟着改齐。
/// 按文本读取，不引用调用端的项目（三个部分互不引用）。
/// </summary>
internal static partial class ContractChecks
{
    /// <summary>
    /// SDK 自己的错误码（没收到服务响应时用），服务端没有。
    /// </summary>
    private static readonly KeyValuePair<string, string>[] ClientOnlyCodes =
    [
        KeyValuePair.Create("NetworkError", "NETWORK_ERROR"),
        KeyValuePair.Create("Timeout", "TIMEOUT"),
    ];

    /// <summary>
    /// 核对 SDK 副本和契约文档。
    /// </summary>
    /// <param name="repositoryRoot">仓库根目录。</param>
    /// <param name="check">记录检查结果。</param>
    public static void Run(string repositoryRoot, Action<string, bool> check)
    {
        string Read(string relative) => File.ReadAllText(Path.Combine(repositoryRoot, relative));
        var contracts = Path.Combine("backend", "src", "ApprovalService.Contracts", "Protocol");
        var sdk = Path.Combine("client", "src", "ApprovalService.Client");

        var headers = Constants(Read(Path.Combine(contracts, "HeaderNames.cs")));
        var sdkHeaders = Constants(Read(Path.Combine(sdk, "Internal", "Protocol.cs")));
        check("SDK 的 HTTP 头和后端 Contracts 一致" + Difference(headers, sdkHeaders), headers.SetEquals(sdkHeaders));

        // SDK 多出的只能是它自己的两个错误码（连不上、超时），服务端不会返回。
        var codes = Constants(Read(Path.Combine(contracts, "ErrorCodes.cs")));
        var sdkCodes = Constants(Read(Path.Combine(sdk, "ApprovalErrorCodes.cs")));
        sdkCodes.ExceptWith(ClientOnlyCodes);
        check("SDK 的错误码和后端 Contracts 一致（SDK 另有 NETWORK_ERROR、TIMEOUT）" + Difference(codes, sdkCodes), codes.SetEquals(sdkCodes));

        var document = Read(Path.Combine("docs", "contracts.md"));
        var undocumented = codes.Concat(ClientOnlyCodes).Select(item => item.Value).Concat(headers.Select(item => item.Value))
            .Where(value => !document.Contains($"`{value}`", StringComparison.Ordinal))
            .ToArray();
        check("docs/contracts.md 写了全部错误码和 HTTP 头" + (undocumented.Length > 0 ? "，缺：" + string.Join("、", undocumented) : ""), undocumented.Length == 0);

        var callbackEvents = Constants(Read(Path.Combine(contracts, "CallbackEventCodes.cs")));
        var undocumentedCallbacks = callbackEvents.Select(item => item.Value)
            .Where(value => !document.Contains($"`{value}`", StringComparison.Ordinal)).ToArray();
        check("docs/contracts.md 写了全部回调事件码" +
            (undocumentedCallbacks.Length > 0 ? "，缺：" + string.Join("、", undocumentedCallbacks) : ""),
            undocumentedCallbacks.Length == 0);

        var flowKinds = Read(Path.Combine("backend", "src", "ApprovalService.Contracts", "Flows", "FlowKinds.cs"));
        var flowCopy = Read(Path.Combine("frontend", "src", "model", "flow.ts"));
        CheckFlowKinds(flowKinds, flowCopy, "FlowNodeTypes", "flowNodeTypes", "节点类型", check);
        CheckFlowKinds(flowKinds, flowCopy, "FlowFieldTypes", "flowFieldTypes", "字段类型", check);
        CheckFlowKinds(flowKinds, flowCopy, "FlowGatewayDirections", "flowGatewayDirections", "网关方向", check);
        CheckFlowKinds(flowKinds, flowCopy, "FlowFieldAccess", "flowFieldAccess", "字段权限", check);
        CheckFlowKinds(flowKinds, flowCopy, "FlowReturnModes", "flowReturnModes", "退回重提方式", check);
        // 执行图与打印类型已按职责拆出，契约核对仍覆盖全部前端副本源码。
        var executionCopy = Read(Path.Combine("frontend", "src", "model", "flow-execution.ts"));
        var printCopy = Read(Path.Combine("frontend", "src", "model", "flow-print.ts"));
        CheckFlowProperties(flowCopy + Environment.NewLine + executionCopy + Environment.NewLine + printCopy, check);

        var approvalKinds = Read(Path.Combine("backend", "src", "ApprovalService.Contracts", "Flows", "FlowApprovalKinds.cs"));
        var approvalCopy = Read(Path.Combine("frontend", "src", "model", "flow-approval-kinds.ts"));
        CheckFlowKinds(approvalKinds, approvalCopy, "FlowAssigneeSources", "flowAssigneeSources", "审批人来源", check);
        CheckFlowKinds(approvalKinds, approvalCopy, "FlowApprovalModes", "flowApprovalModes", "多人审批方式", check);
        CheckFlowKinds(approvalKinds, approvalCopy, "FlowRejectBehaviors", "flowRejectBehaviors", "会签拒绝处理", check);
        CheckFlowKinds(approvalKinds, approvalCopy, "FlowMissingAssigneeBehaviors", "flowMissingAssigneeBehaviors", "无审批人处理", check);
        CheckFlowKinds(approvalKinds, approvalCopy, "FlowSelfApprovalBehaviors", "flowSelfApprovalBehaviors", "本人审批处理", check);
        CheckFlowKinds(approvalKinds, approvalCopy, "FlowDuplicateAssigneeBehaviors", "flowDuplicateAssigneeBehaviors", "重复审批人处理", check);
        CheckFlowKinds(approvalKinds, approvalCopy, "FlowInactiveAssigneeBehaviors", "flowInactiveAssigneeBehaviors", "离职审批人处理", check);

        var backendVersion = Regex.Match(flowKinds, @"public const int CurrentVersion = (?<value>\d+);").Groups["value"].Value;
        var frontendVersion = Regex.Match(flowCopy, @"export const flowSchemaVersion = (?<value>\d+) as const;").Groups["value"].Value;
        check("流程定义格式版本的前端副本和后端一致", backendVersion.Length > 0 && backendVersion == frontendVersion);

        var missingFlowKinds = new[] { "FlowNodeTypes", "FlowFieldTypes", "FlowReturnModes" }
            .SelectMany(name => Constants(ClassBody(flowKinds, name)).Select(item => item.Value))
            .Where(value => !document.Contains($"`{value}`", StringComparison.Ordinal))
            .ToArray();
        check("docs/contracts.md 写了全部流程节点和字段类型" + (missingFlowKinds.Length > 0 ? "，缺：" + string.Join("、", missingFlowKinds) : ""), missingFlowKinds.Length == 0);
        var missingApprovalKinds = Constants(approvalKinds).Select(item => item.Value).Distinct(StringComparer.Ordinal)
            .Where(value => !document.Contains($"`{value}`", StringComparison.Ordinal)).ToArray();
        check("docs/contracts.md 写了全部审批来源与规则编码" + (missingApprovalKinds.Length > 0 ? "，缺：" + string.Join("、", missingApprovalKinds) : ""), missingApprovalKinds.Length == 0);
    }

    /// <summary>
    /// 核对一组流程常量在前后端的名字和值。
    /// </summary>
    /// <param name="backendSource">后端常量源码。</param>
    /// <param name="frontendSource">前端镜像源码。</param>
    /// <param name="backendClass">后端类名。</param>
    /// <param name="frontendObject">前端对象名。</param>
    /// <param name="label">检查项的中文说明。</param>
    /// <param name="check">记录检查结果。</param>
    private static void CheckFlowKinds(string backendSource, string frontendSource, string backendClass, string frontendObject, string label, Action<string, bool> check)
    {
        var backend = Constants(ClassBody(backendSource, backendClass));
        var frontendBody = Regex.Match(frontendSource, $@"export const {Regex.Escape(frontendObject)} = \{{(?<body>.*?)\}} as const;", RegexOptions.Singleline).Groups["body"].Value;
        var frontend = Regex.Matches(frontendBody, @"(?<name>\w+): '(?<value>[^']+)'", RegexOptions.Singleline)
            .Select(match => KeyValuePair.Create(match.Groups["name"].Value, match.Groups["value"].Value)).ToHashSet();
        check($"流程{label}的前端副本和后端一致" + Difference(backend, frontend), backend.Count > 0 && backend.SetEquals(frontend));
    }

    /// <summary>
    /// 从源文件中取出指定静态类的正文。
    /// </summary>
    /// <param name="source">C# 源码。</param>
    /// <param name="className">静态类名。</param>
    /// <returns>类正文；找不到时返回空字符串。</returns>
    private static string ClassBody(string source, string className) =>
        Regex.Match(source, $@"public static class {Regex.Escape(className)}\s*\{{(?<body>.*?)\}}", RegexOptions.Singleline).Groups["body"].Value;

    /// <summary>
    /// 核对前端各流程模型的 JSON 属性名与后端公开属性一一对应。
    /// </summary>
    /// <param name="frontendSource">前端流程类型源码。</param>
    /// <param name="check">记录检查结果。</param>
    private static void CheckFlowProperties(string frontendSource, Action<string, bool> check)
    {
        Type[] models =
        [
            typeof(FlowDefinition), typeof(FlowCanvas), typeof(FlowNode), typeof(FlowEdge),
            typeof(FlowAssignee), typeof(FlowApprovalSettings), typeof(FlowSubprocessSettings),
            typeof(FlowAutomationSettings), typeof(FlowFieldPermission), typeof(FlowDataDefinition),
            typeof(FlowFieldDefinition), typeof(FlowDetailLayout), typeof(FlowLayoutGroup),
            typeof(FlowLayoutItem), typeof(FlowPrintSettings), typeof(FlowExecutionGraph),
        ];

        foreach (var model in models)
        {
            var body = Regex.Match(frontendSource, $@"export interface {Regex.Escape(model.Name)}\s*\{{(?<body>.*?)^\}}", RegexOptions.Singleline | RegexOptions.Multiline).Groups["body"].Value;
            var frontend = Regex.Matches(body, @"^\s*(?<name>\w+)\??\s*:", RegexOptions.Multiline)
                .Select(match => match.Groups["name"].Value).ToHashSet();
            var backend = model.GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToHashSet();
            var missing = backend.Except(frontend).ToArray();
            var extra = frontend.Except(backend).ToArray();
            var difference = missing.Length + extra.Length == 0 ? "" : $"（副本缺：{string.Join("、", missing)}；副本多：{string.Join("、", extra)}）";
            check($"流程模型 {model.Name} 的 JSON 属性与前端副本一致" + difference, body.Length > 0 && backend.SetEquals(frontend));
        }
    }

    /// <summary>
    /// 读出源码里的字符串常量（public const string 名字 = "值"）。
    /// </summary>
    /// <param name="source">C# 源码。</param>
    /// <returns>名字和值的集合。</returns>
    private static HashSet<KeyValuePair<string, string>> Constants(string source) =>
        ConstantPattern().Matches(source).Select(match => KeyValuePair.Create(match.Groups["name"].Value, match.Groups["value"].Value)).ToHashSet();

    /// <summary>
    /// 两边不一致时的说明。
    /// </summary>
    /// <param name="backend">后端的常量。</param>
    /// <param name="copy">副本的常量。</param>
    /// <returns>一致时为空字符串；否则列出多出和缺少的。</returns>
    private static string Difference(HashSet<KeyValuePair<string, string>> backend, HashSet<KeyValuePair<string, string>> copy)
    {
        var missing = backend.Except(copy).Select(item => $"{item.Key}={item.Value}").ToArray();
        var extra = copy.Except(backend).Select(item => $"{item.Key}={item.Value}").ToArray();
        return missing.Length + extra.Length == 0 ? "" : $"（副本缺：{string.Join("、", missing)}；副本多：{string.Join("、", extra)}）";
    }

    /// <summary>
    /// 字符串常量的写法。
    /// </summary>
    /// <returns>编译好的正则，名字在 name 组，值在 value 组。</returns>
    [GeneratedRegex("""public const string (?<name>\w+) = "(?<value>[^"]+)";""")]
    private static partial Regex ConstantPattern();
}
