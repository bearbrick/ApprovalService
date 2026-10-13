namespace ApprovalService.Client;

/// <summary>
/// 服务信息（GET /api/v1/info），接入时先调它确认地址和版本。
/// </summary>
public sealed class ServiceInfo
{
    /// <summary>
    /// 创建服务信息。
    /// </summary>
    /// <param name="name">服务名称。</param>
    /// <param name="version">服务程序的版本。</param>
    /// <param name="apiVersion">开放接口的版本。</param>
    public ServiceInfo(string name, string version, string apiVersion)
    {
        Name = name;
        Version = version;
        ApiVersion = apiVersion;
    }

    /// <summary>
    /// 服务名称，固定为 ApprovalService。
    /// </summary>
    public string Name
    {
        get;
    }

    /// <summary>
    /// 服务程序的版本，如 0.1.0。
    /// </summary>
    public string Version
    {
        get;
    }

    /// <summary>
    /// 开放接口的版本，如 v1。
    /// </summary>
    public string ApiVersion
    {
        get;
    }
}
