using ApprovalService.Application.Administration;
using ApprovalService.Application.Files;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Notifications;
using ApprovalService.Application.Reporting;
using ApprovalService.Domain.Administration;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Files;
using ApprovalService.Domain.Identity;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Reporting;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Administration;
using ApprovalService.Infrastructure.Center;
using ApprovalService.Infrastructure.Files;
using ApprovalService.Infrastructure.Hosting;
using ApprovalService.Infrastructure.Identity;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Notifications;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Organization;
using ApprovalService.Infrastructure.Persistence;
using ApprovalService.Infrastructure.Reporting;
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
        services.AddOptions<BackupOptions>()
            .Bind(configuration.GetSection(BackupOptions.SectionName))
            .Validate(options => TimeOnly.TryParseExact(options.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _),
                "配置 ApprovalService:Backup:Time 必须是 HH:mm。")
            .Validate(options => options.Keep is >= 1 and <= 365,
                "配置 ApprovalService:Backup:Keep 必须介于 1 到 365。")
            .ValidateOnStart();
        services.AddSingleton<SqliteBackupService>();
        services.AddApprovalPersistence(configuration);
        services.AddHostedService<DailyBackupWorker>();
        services.AddScoped<IAdminConsoleRepository, AdminConsoleRepository>();
        services.AddScoped<IReportingFactsRepository, ReportingFactsRepository>();
        services.AddSingleton<IReportingWorkbookExporter, ReportingWorkbookExporter>();
        services.AddScoped<IClientAppRepository, ClientAppRepository>();
        services.AddScoped<IClientAppAdminRepository, ClientAppAdminRepository>();
        services.AddScoped<ITestCallbackRepository, TestCallbackRepository>();
        services.AddScoped<IFlowCatalogRepository, FlowCatalogRepository>();
        services.AddScoped<IFlowManagementRepository, FlowManagementRepository>();
        services.AddSingleton<IFlowSimulationSessionStore, MemoryFlowSimulationSessionStore>();
        services.AddScoped<IFlowTransferRepository, FlowTransferRepository>();
        services.AddScoped<IFlowVersionRepository, FlowVersionRepository>();
        services.AddScoped<FlowAdministratorIdentityReconciler>();
        services.AddScoped<ICallbackDeliveryRepository, CallbackDeliveryRepository>();
        services.AddScoped<IActionRetryRepository, ActionRetryRepository>();
        services.AddScoped<IApprovalOpenQueryRepository, ApprovalOpenQueryRepository>();
        services.AddScoped<ICenterQueryRepository, CenterQueryRepository>();
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
        services.AddSingleton(NotificationCalendarSettings.Create(configuration));
        // 绝不跟随业务系统返回的 307/308，以免签名请求正文被转发到未登记的主机。
        services.AddSingleton(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }));
        services.AddSingleton<SignedHttpOutboundAdapter>();
        services.AddScoped<IApprovalHookClient, ApprovalHookClient>();
        services.AddScoped<IOutboxHandler, CallbackOutboxHandler>();
        services.AddScoped<IOutboxHandler, EngineEventOutboxHandler>();
        services.Configure<IdentityTokenOptions>(configuration.GetSection(IdentityTokenOptions.SectionName));
        services.AddSingleton<JwtIdentityTokenService>();
        services.AddSingleton<IIdentityAccessTokenService>(provider => provider.GetRequiredService<JwtIdentityTokenService>());
        services.AddSingleton<IIdentityPasswordService, Pbkdf2IdentityPasswordService>();
        services.AddScoped<FirstAdminBootstrapper>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .Validate(options => options.Provider is "Local" or "Minio", "附件存储提供者必须是 Local 或 Minio。")
            .Validate(options => options.MaxUploadBytes > 0, "附件大小限制必须是正数。")
            .Validate(options => options.PresignedUrlSeconds >= 1 &&
                options.PresignedUrlSeconds <= FileStorageOptions.MaxPresignedUrlSeconds,
                "附件预签名地址有效期必须为 1 秒到 5 分钟。")
            .Validate(options => options.Provider != "Minio" ||
                (Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) &&
                    endpoint.Scheme is "http" or "https" && endpoint.AbsolutePath == "/" &&
                    endpoint.UserInfo.Length == 0 && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0 &&
                    !string.IsNullOrWhiteSpace(options.Bucket) &&
                    !string.IsNullOrWhiteSpace(options.AccessKey) && !string.IsNullOrWhiteSpace(options.SecretKey)),
                "Minio 存储需要有效地址、桶和访问凭据。")
            .ValidateOnStart();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FileStorageOptions>>().Value;
            return new FilePolicyOptions
            {
                MaxUploadBytes = options.MaxUploadBytes,
                DownloadUrlLifetime = TimeSpan.FromSeconds(options.PresignedUrlSeconds),
            };
        });
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IFileStorage, MinioFileStorage>();
        services.AddSingleton<IFileStorageSelector, FileStorageSelector>();
        services.AddScoped<IStoredFileRepository, StoredFileRepository>();
        services.AddScoped<LocalToMinioMigration>();
        services.AddScoped<IFileMigrationService>(provider => provider.GetRequiredService<LocalToMinioMigration>());
        services.AddHostedService<FileCleanupHostedService>();
        services.AddSingleton<OutboxQueue>();
        services.AddSingleton<ScheduledJobQueue>();
        services.AddScoped<IScheduledJobHandler, DepartureJobHandler>();
        services.AddHostedService<DurableJobWorker>();
        services.AddOptions<SmtpNotificationOptions>()
            .Bind(configuration.GetSection(SmtpNotificationOptions.SectionName));
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationSettingsRepository, NotificationSettingsRepository>();
        services.AddScoped<DailyDigestScheduler>();
        services.AddScoped<IScheduledJobHandler, DailyDigestJobHandler>();
        services.AddHostedService<DailyDigestHostedService>();
        services.AddScoped<INotificationChannel, SmtpNotificationChannel>();
        services.AddScoped<INotificationChannel, InAppNotificationChannel>();
        services.AddScoped<IOutboxHandler, NotificationOutboxHandler>();
        services.AddScoped<IOutboxHandler, DeadlineReminderOutboxHandler>();
        return services;
    }
}
