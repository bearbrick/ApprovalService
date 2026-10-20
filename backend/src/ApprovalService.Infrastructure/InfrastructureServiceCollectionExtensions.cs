using ApprovalService.Application.Files;
using ApprovalService.Domain.Files;
using ApprovalService.Infrastructure.Files;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Options;
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
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .Validate(options => options.Provider is "Local" or "Minio", "附件存储提供者必须是 Local 或 Minio。")
            .Validate(options => options.MaxUploadBytes > 0, "附件大小限制必须是正数。")
            .Validate(options => options.PresignedUrlSeconds is >= 1 and <= 604800,
                "附件预签名地址有效期必须为 1 秒到 7 天。")
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
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IFileStorage, MinioFileStorage>();
        services.AddSingleton<IFileStorageSelector, FileStorageSelector>();
        services.AddScoped<IStoredFileRepository, StoredFileRepository>();
        services.AddScoped<LocalToMinioMigration>();
        services.AddScoped<IFileMigrationService>(provider => provider.GetRequiredService<LocalToMinioMigration>());
        services.AddHostedService<FileCleanupHostedService>();
        services.AddSingleton<OutboxQueue>();
        services.AddSingleton<ScheduledJobQueue>();
        services.AddHostedService<DurableJobWorker>();
        return services;
    }
}
