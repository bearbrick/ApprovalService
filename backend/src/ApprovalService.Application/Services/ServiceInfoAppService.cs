using System.Reflection;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Application.Services;

/// <inheritdoc />
internal sealed class ServiceInfoAppService : IServiceInfoAppService
{
    /// <summary>
    /// 服务名称。
    /// </summary>
    public const string ServiceName = "ApprovalService";

    /// <summary>
    /// 开放接口的版本，和 <see cref="ApiRoutes.OpenApiPrefix"/> 对应。
    /// </summary>
    public const string ApiVersion = "v1";

    /// <summary>
    /// 程序版本（取自程序集的版本信息，去掉编译时附加的提交号），启动后不变，算一次就够。
    /// </summary>
    private static readonly string Version = ReadVersion();

    /// <inheritdoc />
    public ServiceInfoResponse Get() => new(ServiceName, Version, ApiVersion);

    /// <summary>
    /// 读程序集的版本信息。
    /// </summary>
    /// <returns>如 0.1.0；读不到时为 0.0.0。</returns>
    private static string ReadVersion()
    {
        var informational = typeof(ServiceInfoAppService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // .NET SDK 会在版本后面加 “+提交号”，对外只给语义化版本部分。
        return string.IsNullOrEmpty(informational) ? "0.0.0" : informational.Split('+')[0];
    }
}
