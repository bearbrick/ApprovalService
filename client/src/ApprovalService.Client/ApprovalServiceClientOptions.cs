namespace ApprovalService.Client;

/// <summary>
/// 客户端设置。
/// </summary>
public sealed class ApprovalServiceClientOptions
{
    /// <summary>
    /// 服务地址，如 http://approval.example.com/。
    /// </summary>
    public Uri? BaseAddress
    {
        get; set;
    }

    /// <summary>
    /// 接入应用的 API Key（管理后台“接入应用”里生成）。放配置文件或环境变量，不要写进代码。
    /// </summary>
    public string? ApiKey
    {
        get; set;
    }

    /// <summary>
    /// 等待响应的超时，默认 30 秒。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
