using ApprovalService.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Application;

/// <summary>
/// 应用层的注册入口：新的应用服务在这里登记一行（AGENTS.md §5）。
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// 注册应用层的全部服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>同一个服务集合，便于链式调用。</returns>
    public static IServiceCollection AddApprovalApplication(this IServiceCollection services)
    {
        services.AddSingleton<IServiceInfoAppService, ServiceInfoAppService>();
        return services;
    }
}
