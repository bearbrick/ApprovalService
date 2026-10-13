using ApprovalService.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Infrastructure;

/// <summary>
/// 基础设施层的注册入口：仓储、数据库、附件存储、消息渠道、出站适配层等在这里登记一行（AGENTS.md §5）。
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// 注册基础设施层的全部服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置，读 ApprovalService 节。</param>
    /// <returns>同一个服务集合，便于链式调用。</returns>
    public static IServiceCollection AddApprovalInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DataOptions>(configuration.GetSection(DataOptions.SectionName));
        return services;
    }
}
