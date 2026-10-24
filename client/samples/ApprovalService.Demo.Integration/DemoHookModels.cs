using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 本机业务后端使用 .NET 自带的 JSON 编解码，不引入界面或第三方依赖。
/// </summary>
internal static class DemoHookJson
{
    /// <summary>
    /// 读取一类固定钩子请求。
    /// </summary>
    /// <typeparam name="T">请求模型。</typeparam>
    /// <param name="body">验签后的原始请求字节。</param>
    /// <returns>解析后的请求。</returns>
    internal static T Read<T>(byte[] body)
    {
        using var stream = new MemoryStream(body);
        return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(stream)
            ?? throw new SerializationException("业务钩子请求不能为空。"));
    }

    /// <summary>
    /// 把固定响应序列化为待签名的原始字节。
    /// </summary>
    /// <typeparam name="T">响应模型。</typeparam>
    /// <param name="value">响应。</param>
    /// <returns>UTF-8 JSON 字节。</returns>
    internal static byte[] Write<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        return stream.ToArray();
    }
}

/// <summary>
/// 取数钩子的请求。
/// </summary>
[DataContract]
internal sealed class DemoDataHookRequest
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    [DataMember(Name = "flowCode")]
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    [DataMember(Name = "businessKey")]
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 请求 ID，与响应头相同。
    /// </summary>
    [DataMember(Name = "requestId")]
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// 取数钩子的报销数据响应。
/// </summary>
[DataContract]
internal sealed class DemoExpenseDataResponse
{
    /// <summary>
    /// 业务审批数据快照。
    /// </summary>
    [DataMember(Name = "data")]
    public DemoExpenseHookData Data { get; set; } = new();

    /// <summary>
    /// 审批标题。
    /// </summary>
    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 报销流程使用的字段映射。
/// </summary>
[DataContract]
internal sealed class DemoExpenseHookData
{
    /// <summary>
    /// 报销事由。
    /// </summary>
    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 费用类别。
    /// </summary>
    [DataMember(Name = "category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 申请金额。
    /// </summary>
    [DataMember(Name = "claimAmount")]
    public decimal ClaimAmount
    {
        get; set;
    }

    /// <summary>
    /// 票据附件 ID。
    /// </summary>
    [DataMember(Name = "receipt")]
    public string Receipt { get; set; } = string.Empty;

    /// <summary>
    /// 费用明细。
    /// </summary>
    [DataMember(Name = "lines")]
    public List<ExpenseLine> Lines { get; set; } = new();
}

/// <summary>
/// 取数钩子的采购数据响应。
/// </summary>
[DataContract]
internal sealed class DemoPurchaseDataResponse
{
    /// <summary>
    /// 业务审批数据快照。
    /// </summary>
    [DataMember(Name = "data")]
    public DemoPurchaseHookData Data { get; set; } = new();

    /// <summary>
    /// 审批标题。
    /// </summary>
    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 采购流程使用的字段映射。
/// </summary>
[DataContract]
internal sealed class DemoPurchaseHookData
{
    /// <summary>
    /// 采购事由。
    /// </summary>
    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 本应用申请人 ID。
    /// </summary>
    [DataMember(Name = "requester")]
    public string Requester { get; set; } = string.Empty;

    /// <summary>
    /// 本应用部门 ID。
    /// </summary>
    [DataMember(Name = "department")]
    public string Department { get; set; } = string.Empty;

    /// <summary>
    /// 未税金额。
    /// </summary>
    [DataMember(Name = "baseAmount")]
    public decimal BaseAmount
    {
        get; set;
    }

    /// <summary>
    /// 税额。
    /// </summary>
    [DataMember(Name = "taxAmount")]
    public decimal TaxAmount
    {
        get; set;
    }

    /// <summary>
    /// 采购明细。
    /// </summary>
    [DataMember(Name = "items")]
    public List<PurchaseLine> Items { get; set; } = new();
}

/// <summary>
/// 校验钩子的请求；数据本身来自业务原单，不信任请求里重新提供的金额。
/// </summary>
[DataContract]
internal sealed class DemoValidationHookRequest
{
    /// <summary>
    /// 校验阶段。
    /// </summary>
    [DataMember(Name = "phase")]
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    [DataMember(Name = "flowCode")]
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    [DataMember(Name = "businessKey")]
    public string BusinessKey { get; set; } = string.Empty;
}

/// <summary>
/// 校验钩子的决定。
/// </summary>
[DataContract]
internal sealed class DemoValidationResponse
{
    /// <summary>
    /// 是否允许推进。
    /// </summary>
    [DataMember(Name = "allowed")]
    public bool Allowed
    {
        get; set;
    }

    /// <summary>
    /// 拒绝原因。
    /// </summary>
    [DataMember(Name = "reason", EmitDefaultValue = false)]
    public string? Reason
    {
        get; set;
    }
}

/// <summary>
/// 业务取人钩子的请求。
/// </summary>
[DataContract]
internal sealed class DemoAssigneeHookRequest
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    [DataMember(Name = "flowCode")]
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    [DataMember(Name = "businessKey")]
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 实际命中的节点 ID。
    /// </summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 节点传入的可选项目编码。
    /// </summary>
    [DataMember(Name = "sourceValue", EmitDefaultValue = false)]
    public string? SourceValue
    {
        get; set;
    }
}

/// <summary>
/// 业务取人钩子的响应。
/// </summary>
[DataContract]
internal sealed class DemoAssigneeResponse
{
    /// <summary>
    /// 本应用已有绑定的业务用户 ID。
    /// </summary>
    [DataMember(Name = "userIds")]
    public List<string> UserIds { get; set; } = new();
}

/// <summary>
/// 自动业务动作钩子的请求。
/// </summary>
[DataContract]
internal sealed class DemoActionHookRequest
{
    /// <summary>
    /// 稳定动作事件 ID。
    /// </summary>
    [DataMember(Name = "eventId")]
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 动作编码。
    /// </summary>
    [DataMember(Name = "actionCode")]
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    [DataMember(Name = "flowCode")]
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    [DataMember(Name = "businessKey")]
    public string BusinessKey { get; set; } = string.Empty;
}

/// <summary>
/// 自动业务动作的结果。
/// </summary>
[DataContract]
internal sealed class DemoActionResponse
{
    /// <summary>
    /// 是否成功。
    /// </summary>
    [DataMember(Name = "success")]
    public bool Success
    {
        get; set;
    }

    /// <summary>
    /// 失败原因。
    /// </summary>
    [DataMember(Name = "message", EmitDefaultValue = false)]
    public string? Message
    {
        get; set;
    }

    /// <summary>
    /// 成功时独立的订单号结果。
    /// </summary>
    [DataMember(Name = "result", EmitDefaultValue = false)]
    public DemoOrderResult? Result
    {
        get; set;
    }
}

/// <summary>
/// 业务动作生成的订单信息。
/// </summary>
[DataContract]
internal sealed class DemoOrderResult
{
    /// <summary>
    /// 采购订单号。
    /// </summary>
    [DataMember(Name = "orderNo")]
    public string OrderNo { get; set; } = string.Empty;
}
