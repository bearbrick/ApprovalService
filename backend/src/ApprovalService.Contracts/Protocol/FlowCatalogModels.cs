using System.Text.Json;
using ApprovalService.Contracts.Flows;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 接入应用已获授权的发布流程。
/// </summary>
public sealed class AvailableFlowResponse
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 流程名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 当前发布版本号。
    /// </summary>
    public int Version
    {
        get; set;
    }
}

/// <summary>
/// 流程发起时需提交的数据定义与可复制的 JSON 样例。
/// </summary>
public sealed class FlowSchemaResponse
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 当前发布版本号。
    /// </summary>
    public int Version
    {
        get; set;
    }

    /// <summary>
    /// 审批数据字段与摘要字段定义。
    /// </summary>
    public FlowDataDefinition Data { get; set; } = new();

    /// <summary>
    /// 按当前字段定义生成的 JSON 样例。
    /// </summary>
    public JsonElement Example
    {
        get; set;
    }
}
