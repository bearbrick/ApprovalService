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
}
