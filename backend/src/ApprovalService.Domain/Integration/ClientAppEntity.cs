namespace ApprovalService.Domain.Integration;

/// <summary>
/// 已登记业务系统及其开放接口权限，签名密钥只保存加密后的密文。
/// </summary>
public sealed class ClientAppEntity
{
    /// <summary>
    /// 接入应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 接入应用名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// API Key 的 SHA-256 小写十六进制摘要。
    /// </summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>
    /// 加密保存的回调与钩子签名密钥。
    /// </summary>
    public string SigningSecretCiphertext { get; set; } = string.Empty;

    /// <summary>
    /// 允许使用的流程编码 JSON 数组。
    /// </summary>
    public string AllowedFlowCodesJson { get; set; } = "[]";

    /// <summary>
    /// 允许嵌入审批中心的完整网站来源 JSON 数组；默认不允许外部网站嵌入。
    /// </summary>
    public string AllowedEmbedOriginsJson { get; set; } = "[]";

    /// <summary>
    /// 允许作为本应用只读原单 iframe 子页面的 HTTPS 来源。
    /// </summary>
    public string AllowedDocumentOriginsJson { get; set; } = "[]";

    /// <summary>
    /// 有权管理本应用的内部人员 ID JSON 数组；存量应用默认无人授权。
    /// </summary>
    public string AdministratorPersonIdsJson { get; set; } = "[]";

    /// <summary>
    /// 是否可以查看所有接入应用的待办数量和列表。
    /// </summary>
    public bool CanViewAllTasks
    {
        get; set;
    }

    /// <summary>
    /// 是否允许调用开放接口。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 每次启停或轮换密钥时单调递增的嵌入身份代际。
    /// </summary>
    public long AuthGeneration { get; set; } = 1;

    /// <summary>
    /// 审批结果回调地址。
    /// </summary>
    public string? CallbackUrl
    {
        get; set;
    }

    /// <summary>
    /// 只传单号发起时的取数接口地址。
    /// </summary>
    public string? DataUrl
    {
        get; set;
    }

    /// <summary>
    /// 发起或同意前的校验钩子地址。
    /// </summary>
    public string? ValidationUrl
    {
        get; set;
    }

    /// <summary>
    /// 流程从业务系统取审批人的接口地址。
    /// </summary>
    public string? AssigneeUrl
    {
        get; set;
    }

    /// <summary>
    /// 自动节点调用业务动作的接口地址。
    /// </summary>
    public string? ActionUrl
    {
        get; set;
    }

    /// <summary>
    /// 按当前查看人签发一次性只读原单地址的钩子。
    /// </summary>
    public string? DocumentUrl
    {
        get; set;
    }
}
