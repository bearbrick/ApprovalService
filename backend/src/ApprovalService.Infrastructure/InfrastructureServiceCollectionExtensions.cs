using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Domain.Identity;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Identity;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Organization;
using ApprovalService.Infrastructure.Persistence;
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
        // 在监听请求前拒绝错误配置，避免健康检查误报就绪；数据库连通性由持久化任务接入后另行检查。
        services.AddOptions<DataOptions>()
            .Bind(configuration.GetSection(DataOptions.SectionName))
            .Validate(options => options.Provider is "Sqlite" or "SqlServer" or "PostgreSql",
                "配置 ApprovalService:Data:Provider 必须是 Sqlite、SqlServer 或 PostgreSql。")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Directory),
                "配置 ApprovalService:Data:Directory 不能为空。")
            .Validate(options => options.Provider is not ("SqlServer" or "PostgreSql") || !string.IsNullOrWhiteSpace(options.ConnectionString),
                "配置 SqlServer 或 PostgreSql 时，ApprovalService:Data:ConnectionString 不能为空。")
            .ValidateOnStart();
        services.AddApprovalPersistence(configuration);
        services.AddScoped<IClientAppRepository, ClientAppRepository>();
        services.AddScoped<IFlowCatalogRepository, FlowCatalogRepository>();
        services.AddScoped<ICallbackDeliveryRepository, CallbackDeliveryRepository>();
        services.AddScoped<IApprovalOpenQueryRepository, ApprovalOpenQueryRepository>();
        services.AddScoped<IApprovalSerialNumberRepository, ApprovalSerialNumberRepository>();
        services.AddSingleton(new IntegrationTransportPolicy
        {
            AllowHttpUrls = configuration.GetValue<bool>("ApprovalService:Integration:AllowHttpUrls"),
        });
        services.AddSingleton(new OpenStartSerialOptions
        {
            Prefix = configuration["ApprovalService:Integration:SerialPrefix"] ?? "SP",
            DatePattern = configuration["ApprovalService:Integration:SerialDatePattern"] ?? "yyyyMM",
            Digits = configuration.GetValue<int?>("ApprovalService:Integration:SerialDigits") ?? 6,
        });
        services.AddSingleton<IIntegrationSecretProtector, AesIntegrationSecretProtector>();
        services.AddSingleton(TimeProvider.System);
        // 绝不跟随业务系统返回的 307/308，以免签名请求正文被转发到未登记的主机。
        services.AddSingleton(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }));
        services.AddSingleton<SignedHttpOutboundAdapter>();
        services.AddScoped<IOutboxHandler, CallbackOutboxHandler>();
        services.AddScoped<IOutboxHandler, EngineEventOutboxHandler>();
        services.Configure<IdentityTokenOptions>(configuration.GetSection(IdentityTokenOptions.SectionName));
        services.AddSingleton<JwtIdentityTokenService>();
        services.AddSingleton<IIdentityAccessTokenService>(provider => provider.GetRequiredService<JwtIdentityTokenService>());
        services.AddSingleton<IIdentityPasswordService, Pbkdf2IdentityPasswordService>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddSingleton<OutboxQueue>();
        services.AddSingleton<ScheduledJobQueue>();
        services.AddHostedService<DurableJobWorker>();
        return services;
    }
}
