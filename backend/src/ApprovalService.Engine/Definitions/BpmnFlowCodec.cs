using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Definitions;

/// <summary>
/// 在 BPMN 2.0 常用图形与审批服务的完整流程定义之间做无损导入导出。
/// </summary>
public static class BpmnFlowCodec
{
    /// <summary>
    /// BPMN 模型命名空间。
    /// </summary>
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    /// <summary>
    /// 审批服务扩展命名空间。
    /// </summary>
    private static readonly XNamespace Approval = "urn:approvalservice:flow:1";

    /// <summary>
    /// XML Schema 实例命名空间，用于标明标准 BPMN 条件表达式类型。
    /// </summary>
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// 单个导入文件允许的最多字符数，避免文件解析耗尽内存。
    /// </summary>
    private const int MaxFileLength = 4 * 1024 * 1024;

    /// <summary>
    /// 把自由画布导出为带完整审批配置的 BPMN 2.0 文件内容。
    /// </summary>
    /// <param name="flow">待导出的正式流程定义。</param>
    /// <returns>BPMN 2.0 XML 内容。</returns>
    public static string Export(FlowDefinition flow)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var json = JsonSerializer.Serialize(flow, ApprovalJson.Options);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var process = new XElement(Bpmn + "process",
            new XAttribute("id", ToXmlId("Process", flow.Code)),
            new XAttribute("name", flow.Name),
            new XAttribute("isExecutable", "true"),
            new XElement(Bpmn + "extensionElements",
                new XElement(Approval + "flowDefinition",
                    new XAttribute("producer", "ApprovalService"),
                    new XAttribute("formatVersion", "1"),
                    new XAttribute("schemaVersion", flow.SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("encoding", "base64-utf8-json"),
                    new XAttribute("sha256", fingerprint),
                    payload)));

        foreach (var node in flow.Canvas.Nodes)
        {
            process.Add(CreateNode(node, flow.Canvas.Edges));
        }

        foreach (var edge in flow.Canvas.Edges)
        {
            process.Add(CreateEdge(edge));
        }

        var definitions = new XElement(Bpmn + "definitions",
            new XAttribute(XNamespace.Xmlns + "bpmn", Bpmn.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "approval", Approval.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute("id", ToXmlId("Definitions", flow.Code)),
            new XAttribute("targetNamespace", Approval.NamespaceName),
            new XAttribute("exporter", "ApprovalService"),
            new XAttribute("exporterVersion", "1"),
            process);

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine
            + definitions.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// 仅导入本系统导出的兼容 BPMN 文件，并验证图形与扩展定义相符。
    /// </summary>
    /// <param name="xml">BPMN 2.0 XML 内容。</param>
    /// <returns>文件内保存的完整流程定义。</returns>
    /// <exception cref="FormatException">文件不是本系统导出的，或文件被改动、格式不兼容。</exception>
    public static FlowDefinition Import(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxFileLength)
        {
            throw new FormatException("BPMN 文件为空或过大，只能导入审批服务导出的兼容文件。");
        }

        XDocument document;
        try
        {
            using var source = new StringReader(xml);
            using var reader = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxFileLength
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new FormatException("BPMN 文件不是合法的 XML。", exception);
        }

        var root = document.Root;
        var process = root?.Element(Bpmn + "process");
        var extension = process?.Element(Bpmn + "extensionElements")?.Element(Approval + "flowDefinition");
        if (root?.Name != Bpmn + "definitions" || extension is null
            || (string?)root.Attribute("exporter") != "ApprovalService"
            || (string?)extension.Attribute("producer") != "ApprovalService")
        {
            throw new FormatException("只能导入审批服务导出的 BPMN 2.0 文件。");
        }

        if ((string?)extension.Attribute("formatVersion") != "1"
            || (string?)extension.Attribute("schemaVersion") != "1"
            || (string?)extension.Attribute("encoding") != "base64-utf8-json")
        {
            throw new FormatException("BPMN 文件版本不兼容，请使用审批服务支持的导出版本。");
        }

        FlowDefinition flow;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(extension.Value));
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            if ((string?)extension.Attribute("sha256") != fingerprint)
            {
                throw new FormatException("BPMN 文件中的审批配置校验失败，文件可能已被修改。");
            }

            flow = JsonSerializer.Deserialize<FlowDefinition>(json, ApprovalJson.Options)
                ?? throw new FormatException("BPMN 文件中的审批配置为空。");
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or DecoderFallbackException)
        {
            throw new FormatException("BPMN 文件中的审批配置无效，文件可能已被修改。", exception);
        }

        if (flow.SchemaVersion != FlowSchema.CurrentVersion || flow.Canvas?.Nodes is null || flow.Canvas.Edges is null)
        {
            throw new FormatException("BPMN 文件中的流程定义版本不兼容。");
        }

        // 从扩展定义重新生成标准图，再比较整个 XML，防止有人只改图而没改扩展。
        XDocument expected;
        try
        {
            expected = XDocument.Parse(Export(flow));
        }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException)
        {
            throw new FormatException("BPMN 文件中的审批配置无效，无法还原流程图。", exception);
        }
        if (!XNode.DeepEquals(root, expected.Root))
        {
            throw new FormatException("BPMN 图与审批服务扩展定义不一致，文件可能已被修改。");
        }

        return flow;
    }

    /// <summary>
    /// 为画布节点创建对应的 BPMN 元素。
    /// </summary>
    /// <param name="node">画布节点。</param>
    /// <param name="edges">当前画布的连线，用于标注网关的兜底出口。</param>
    /// <returns>对应的 BPMN 元素。</returns>
    private static XElement CreateNode(FlowNode node, IReadOnlyList<FlowEdge> edges)
    {
        var elementName = node.Type switch
        {
            FlowNodeTypes.Start => "startEvent",
            FlowNodeTypes.End => "endEvent",
            FlowNodeTypes.Approval or FlowNodeTypes.Handling => "userTask",
            FlowNodeTypes.Cc => "sendTask",
            FlowNodeTypes.ExclusiveGateway => "exclusiveGateway",
            FlowNodeTypes.InclusiveGateway => "inclusiveGateway",
            FlowNodeTypes.ParallelGateway => "parallelGateway",
            FlowNodeTypes.Subprocess => "callActivity",
            FlowNodeTypes.Automatic => "serviceTask",
            _ => throw new ArgumentException($"未知的流程节点类型：{node.Type}。", nameof(node))
        };

        var element = new XElement(Bpmn + elementName,
            new XAttribute("id", ToXmlId("Node", node.Id)),
            new XAttribute("name", node.Name));

        if (node.Type == FlowNodeTypes.Subprocess && !string.IsNullOrWhiteSpace(node.Subprocess?.FlowCode))
        {
            element.Add(new XAttribute("calledElement", ToXmlId("Process", node.Subprocess.FlowCode)));
        }

        if (elementName == "exclusiveGateway" || elementName == "inclusiveGateway" || elementName == "parallelGateway")
        {
            var direction = node.GatewayDirection switch
            {
                FlowGatewayDirections.Split => "Diverging",
                FlowGatewayDirections.Join => "Converging",
                _ => "Unspecified"
            };
            element.Add(new XAttribute("gatewayDirection", direction));

            var defaultEdge = edges.FirstOrDefault(edge => edge.SourceId == node.Id && edge.IsDefault);
            if (defaultEdge is not null)
            {
                element.Add(new XAttribute("default", ToXmlId("Flow", defaultEdge.Id)));
            }
        }

        return element;
    }

    /// <summary>
    /// 为画布连线创建 BPMN 顺序流元素。
    /// </summary>
    /// <param name="edge">画布连线。</param>
    /// <returns>BPMN 顺序流元素。</returns>
    private static XElement CreateEdge(FlowEdge edge)
    {
        var element = new XElement(Bpmn + "sequenceFlow",
            new XAttribute("id", ToXmlId("Flow", edge.Id)),
            new XAttribute("sourceRef", ToXmlId("Node", edge.SourceId)),
            new XAttribute("targetRef", ToXmlId("Node", edge.TargetId)));

        if (!string.IsNullOrWhiteSpace(edge.Condition))
        {
            element.Add(new XElement(Bpmn + "conditionExpression",
                new XAttribute(Xsi + "type", "bpmn:tFormalExpression"),
                edge.Condition));
        }

        return element;
    }

    /// <summary>
    /// 把任意流程标识转换成稳定、合法的 XML 标识。
    /// </summary>
    /// <param name="prefix">标识类别。</param>
    /// <param name="value">原始标识。</param>
    /// <returns>可用于 BPMN 引用的标识。</returns>
    private static string ToXmlId(string prefix, string value)
    {
        return prefix + "_" + Convert.ToHexString(Encoding.UTF8.GetBytes(value));
    }
}
