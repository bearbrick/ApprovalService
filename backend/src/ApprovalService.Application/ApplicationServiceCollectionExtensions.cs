using ApprovalService.Application.Administration;
using ApprovalService.Application.Center;
using ApprovalService.Application.Files;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Notifications;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Reporting;
using ApprovalService.Application.Runtime;
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
        services.AddScoped<IServiceReadinessAppService, ServiceReadinessAppService>();
        services.AddScoped<IApprovalRuntimeAppService, ApprovalRuntimeAppService>();
        services.AddScoped<ClientAppAppService>();
        services.AddScoped<ClientAppAdminAppService>();
        services.AddScoped<TestCallbackAppService>();
        services.AddScoped<FlowCatalogAppService>();
        services.AddScoped<OpenFlowPreviewAppService>();
        services.AddScoped<CallbackDeliveryAppService>();
        services.AddScoped<ActionRetryAppService>();
        services.AddScoped<OpenApprovalQueryAppService>();
        services.AddScoped<CenterQueryAppService>();
        services.AddScoped<CenterActionAppService>();
        services.AddScoped<CenterOpinionAppService>();
        services.AddScoped<OpenApprovalStartAppService>();
        services.AddScoped<BusinessHookAppService>();
        services.AddScoped<BusinessAssigneeAppService>();
        services.AddScoped<OpenInstanceCommandAppService>();
        services.AddScoped<OpenDataPatchAppService>();
        services.AddScoped<OpenResubmitAppService>();
        services.AddScoped<OpenTaskActionAppService>();
        services.AddScoped<OpenStartContextBuilder>();
        services.AddSingleton<IMatrixMatchProvider, UnconfiguredMatrixMatchProvider>();
        services.AddScoped<IOrganizationAppService, OrganizationAppService>();
        services.AddScoped<IOrganizationLookupAppService, OrganizationLookupAppService>();
        services.AddScoped<IIdentityAppService, IdentityAppService>();
        services.AddScoped<INotificationAppService, NotificationAppService>();
        services.AddScoped<INotificationSettingsAppService, NotificationSettingsAppService>();
        services.AddScoped<IFileAppService, FileAppService>();
        services.AddScoped<AdminConsoleAppService>();
        services.AddScoped<ReportingQueryAppService>();
        services.AddScoped<FlowManagementAppService>();
        services.AddScoped<FlowSimulationAppService>();
        services.AddScoped<FlowTransferAppService>();
        services.AddScoped<FlowVersionAppService>();
        return services;
    }
}
