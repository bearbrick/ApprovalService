namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 组织管理员登记接入应用的非密钥资料。
/// </summary>
public sealed class AdminClientAppCreateRequest
{
    /// <summary>
    /// 唯一应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>
    /// 应用显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 可使用的已发布流程编码。
    /// </summary>
    public IReadOnlyList<string> AllowedFlowCodes { get; set; } = [];
    /// <summary>
    /// 是否允许跨应用统计待办。
    /// </summary>
    public bool CanViewAllTasks
    {
        get; set;
    }
    /// <summary>
    /// 审批结果回调地址。
    /// </summary>
    public string? CallbackUrl
    {
        get; set;
    }
    /// <summary>
    /// 取数钩子地址。
    /// </summary>
    public string? DataUrl
    {
        get; set;
    }
    /// <summary>
    /// 校验钩子地址。
    /// </summary>
    public string? ValidationUrl
    {
        get; set;
    }
    /// <summary>
    /// 取人钩子地址。
    /// </summary>
    public string? AssigneeUrl
    {
        get; set;
    }
    /// <summary>
    /// 自动动作地址。
    /// </summary>
    public string? ActionUrl
    {
        get; set;
    }
    /// <summary>
    /// 管理员填写的登记原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 管理员启停应用的请求。
/// </summary>
public sealed class AdminClientAppEnabledRequest
{
    /// <summary>
    /// 目标启用状态。
    /// </summary>
    public bool Enabled
    {
        get; set;
    }
    /// <summary>
    /// 状态变更原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 管理员轮换密钥的请求。
/// </summary>
public sealed class AdminClientAppRotateRequest
{
    /// <summary>
    /// 密钥轮换原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 仅在应用创建或密钥轮换的这次响应中显示的凭据。
/// </summary>
public sealed class AdminClientAppSecretsResponse
{
    /// <summary>
    /// 应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>
    /// 只显示一次的 API Key。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>
    /// 只显示一次的 HMAC 签名密钥。
    /// </summary>
    public string SigningSecret { get; set; } = string.Empty;
}
