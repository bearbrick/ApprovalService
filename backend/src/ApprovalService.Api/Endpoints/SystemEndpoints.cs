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
        return app;
    }
}
