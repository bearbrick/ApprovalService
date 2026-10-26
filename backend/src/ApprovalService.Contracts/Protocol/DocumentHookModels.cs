namespace ApprovalService.Contracts.Protocol;
/// <summary>
/// 审批服务向业务系统申请当前查看人的一次性只读原单地址。
/// </summary>
public sealed class DocumentHookRequest
{
    /// <summary>
    /// 本次签名钩子请求 ID。
    /// </summary>
    public string RequestId { get; set; } = string.Empty;
    /// <summary>
    /// 已授权审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
    /// <summary>
    /// 审批单锁定的流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;
    /// <summary>
    /// 业务系统自己的单据号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;
    /// <summary>
    /// 发布版本登记的业务页面编码。
    /// </summary>
    public string EmbedPageCode { get; set; } = string.Empty;
    /// <summary>
    /// 本应用已绑定的当前查看人外部 ID。
    /// </summary>
    public string ViewerExternalId { get; set; } = string.Empty;
    /// <summary>
    /// 当前查看人能看到的顶层字段和明细列路径。
    /// </summary>
    public IReadOnlyList<string> VisibleFieldPaths { get; set; } = [];
    /// <summary>
    /// 固定为真，业务系统不得签发可写页面。
    /// </summary>
    public bool ReadOnly { get; set; } = true;
}
/// <summary>
/// 业务系统返回的短时一次性只读原单地址。
/// </summary>
public sealed class DocumentHookResponse
{
    /// <summary>
    /// HTTPS 一次性地址。
    /// </summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>
    /// UTC 到期时间，不超过签发后五分钟。
    /// </summary>
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }
}
/// <summary>
/// 审批中心给当前人员的一次性原单地址，禁止缓存。
/// </summary>
public sealed class CenterDocumentUrlResponse
{
    /// <summary>
    /// 经来源校验的只读页面地址。
    /// </summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>
    /// 业务系统声明的失效时间。
    /// </summary>
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }
}
/// <summary>
/// 管理员可维护的本应用原单钩子和子页面来源。
/// </summary>
public sealed class AdminDocumentSettingsResponse
{
    /// <summary>
    /// 接入应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>
    /// 可空的完整钩子地址。
    /// </summary>
    public string? DocumentUrl
    {
        get; set;
    }
    /// <summary>
    /// 完整 HTTPS 来源列表。
    /// </summary>
    public IReadOnlyList<string> AllowedDocumentOrigins { get; set; } = [];
}
/// <summary>
/// 管理员完整替换本应用原单设置的请求。
/// </summary>
public sealed class UpdateAdminDocumentSettingsRequest
{
    /// <summary>
    /// 可空的完整钩子地址。
    /// </summary>
    public string? DocumentUrl
    {
        get; set;
    }
    /// <summary>
    /// 完整 HTTPS 来源列表。
    /// </summary>
    public IReadOnlyList<string>? AllowedDocumentOrigins
    {
        get; set;
    }
    /// <summary>
    /// 本次设置变更的原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
