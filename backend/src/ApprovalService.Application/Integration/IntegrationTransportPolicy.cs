namespace ApprovalService.Application.Integration;

/// <summary>
/// 出站地址的部署级传输策略；默认只准 HTTPS。
/// </summary>
public sealed class IntegrationTransportPolicy
{
    /// <summary>
    /// 在受信任内网显式允许 HTTP 回调和钩子地址。
    /// </summary>
    public bool AllowHttpUrls
    {
        get; set;
    }
}
