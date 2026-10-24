using System.Net;
using System.Security.Cryptography;
using System.Text;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Services;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 服务自身的接口：健康检查和服务信息，不用 API Key。
/// </summary>
internal static class SystemEndpoints
{
    /// <summary>
    /// 登记健康检查和服务信息接口。
    /// </summary>
    /// <param name="app">路由。</param>
    /// <returns>同一个路由，便于链式调用。</returns>
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.HealthLive, () => Results.Text("live"));
        app.MapGet(ApiRoutes.HealthReady, async (IServiceReadinessAppService service, CancellationToken cancellationToken) =>
            await service.IsReadyAsync(cancellationToken)
                ? Results.Text("ready")
                : Results.Text("not ready", statusCode: StatusCodes.Status503ServiceUnavailable));
        app.MapGet(ApiRoutes.Info, (IServiceInfoAppService service) => service.Get());
        app.MapGet(ApiRoutes.Metrics, MetricsAsync);
        return app;
    }

    /// <summary>
    /// 只向本机或持部署级采集密钥的采集器输出不含业务标识的聚合指标。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <param name="metrics">进程内指标聚合器。</param>
    /// <param name="configuration">部署级采集密钥。</param>
    /// <returns>指标文本或统一的拒绝错误。</returns>
    private static Task MetricsAsync(HttpContext http, RequestMetrics metrics, IConfiguration configuration)
    {
        var address = http.Connection.RemoteIpAddress;
        if ((address is null || !IPAddress.IsLoopback(address)) && !HasMetricsToken(http, configuration))
        {
            return ApiErrors.WriteAsync(http, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "指标接口只允许本机或已配置的采集器访问。");
        }

        http.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        return http.Response.WriteAsync(metrics.Render());
    }

    /// <summary>
    /// 恒定时间比较部署密钥，未配置或太短时不开放远程采集。
    /// </summary>
    /// <param name="http">采集请求。</param>
    /// <param name="configuration">部署环境变量。</param>
    /// <returns>是否持有有效的采集密钥。</returns>
    private static bool HasMetricsToken(HttpContext http, IConfiguration configuration)
    {
        var expected = configuration["ApprovalService:Metrics:BearerToken"];
        var supplied = http.Request.Headers.Authorization.ToString();
        if (expected is null || expected.Length < 32 || !supplied.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return false;
        }

        var actual = supplied[7..];
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
