using System.Globalization;
using System.Security.Claims;
using ApprovalService.Api.Endpoints;
using ApprovalService.Api.Endpoints.Center;
using ApprovalService.Api.Endpoints.Files;
using ApprovalService.Api.Endpoints.OpenApi;
using ApprovalService.Api.Hosting;
using ApprovalService.Application;
using ApprovalService.Application.Files;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Infrastructure;
using ApprovalService.Infrastructure.Hosting;
using ApprovalService.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

var bootstrapCommand = args.Length > 0 && args[0] == "bootstrap-admin";
var reconcileFlowAdministrators = args.Length > 0 && args[0] == "reconcile-flow-admins";
var migrateFiles = args.Contains("--migrate-files", StringComparer.Ordinal);
var backupNow = args.Length == 1 && args[0] == "backup-now";
var builder = WebApplication.CreateBuilder(bootstrapCommand || reconcileFlowAdministrators ? [] :
    args.Where(arg => arg is not ("--migrate-files" or "backup-now")).ToArray());
builder.Host.UseWindowsService(options => options.ServiceName = "ApprovalService");
if (bootstrapCommand)
{
    // 首次迁移会执行大量 SQL，控制台只保留告警，避免冲掉交互提示。
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
}

// 三层各一个注册入口（docs/architecture.md）；接口的 JSON 写法和 Contracts 的约定保持一致。
builder.Services.AddApprovalApplication();
builder.Services.AddApprovalInfrastructure(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => ApprovalJson.Configure(options.SerializerOptions));
builder.Services.AddSingleton<RequestMetrics>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtIdentityTokenService>((options, tokenService) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokenService.ValidationParameters;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var personId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                var versionText = context.Principal?.FindFirstValue(JwtIdentityTokenService.TokenVersionClaim);
                var sessionKind = context.Principal?.FindFirstValue(JwtIdentityTokenService.SessionKindClaim);
                var clientAppCode = context.Principal?.FindFirstValue(JwtIdentityTokenService.ClientAppCodeClaim);
                var externalUserId = context.Principal?.FindFirstValue(JwtIdentityTokenService.ExternalUserIdClaim);
                var generationText = context.Principal?.FindFirstValue(JwtIdentityTokenService.AuthGenerationClaim);
                long? authGeneration = null;
                if (generationText is not null)
                {
                    if (!long.TryParse(generationText, NumberStyles.None, CultureInfo.InvariantCulture,
                        out var parsedGeneration))
                    {
                        context.Fail("访问令牌中的应用身份代际无效。");
                        return;
                    }

                    authGeneration = parsedGeneration;
                }

                if (string.IsNullOrWhiteSpace(personId) ||
                    !long.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
                    !await context.HttpContext.RequestServices.GetRequiredService<IIdentityAppService>()
                        .ValidateAccessPrincipalAsync(personId, version, sessionKind, clientAppCode,
                            externalUserId, authGeneration, context.HttpContext.RequestAborted))
                {
                    context.Fail("访问令牌已作废或人员已停用。");
                    return;
                }

                if (context.Principal?.FindFirstValue(JwtIdentityTokenService.MustChangePasswordClaim) == "true" &&
                    context.HttpContext.Request.Path != ApiRoutes.IdentityChangePassword)
                {
                    context.Fail("首次登录须先修改密码。");
                    return;
                }

                // 内部 API 一律从这个声明读取人员 ID，避免把客户端传来的用户 ID 当作已认证身份。
                ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, personId));
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                return ApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                    ErrorCodes.Unauthorized, "请先登录或重新登录。");
            },
            OnForbidden = context => ApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden, "当前人员没有操作权限。"),
        };
    });
builder.Services.AddAuthorization(options => options.AddPolicy(OrganizationAdminRequirement.PolicyName,
    policy => policy.RequireAuthenticatedUser().AddRequirements(new OrganizationAdminRequirement())));
builder.Services.AddScoped<IAuthorizationHandler, OrganizationAdminAuthorizationHandler>();

var app = builder.Build();

// 默认页面安全头包住异常处理；请求 ID 和链路 ID 紧随异常处理，出错响应也带上。
app.UseFrontendDocumentSecurity();
app.UseRouting();
app.UseMiddleware<RequestMetricsMiddleware>();
app.UseExceptionHandler(errorApp => errorApp.Run(ApiErrors.WriteUnhandledAsync));
app.UseMiddleware<RequestTraceMiddleware>();
app.UseStatusCodePages(context => ApiErrors.WriteStatusAsync(context.HttpContext));
app.UseAuthentication();
app.UseAuthorization();
app.UseApprovalFrontend();

app.MapSystemEndpoints();
app.MapOpenFlowEndpoints();
app.MapOpenInstanceQueryEndpoints();
app.MapOpenTaskEndpoints();
app.MapOpenStartEndpoints();
app.MapOpenInstanceCommandEndpoints();
app.MapOpenTaskActionEndpoints();
app.MapIdentityEndpoints();
app.MapNotificationSettingsEndpoints();
app.MapOrganizationEndpoints();
app.MapActionRetryEndpoints();
app.MapAdminConsoleEndpoints();
app.MapClientAppAdminEndpoints();
app.MapReportingEndpoints();
app.MapFlowManagementEndpoints();
app.MapFlowSimulationEndpoints();
app.MapFileEndpoints();
app.MapCenterQueryEndpoints();
app.MapCenterActionEndpoints();
app.MapCenterOpinionEndpoints();
app.MapCenterEmbedEndpoints();

if (bootstrapCommand)
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("用法：dotnet run --project backend/src/ApprovalService.Api -- bootstrap-admin");
        Environment.ExitCode = 1;
        return;
    }

    Environment.ExitCode = await BootstrapAdminCommand.RunAsync(app.Services);
    return;
}

if (reconcileFlowAdministrators)
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("用法：dotnet run --project backend/src/ApprovalService.Api -- reconcile-flow-admins");
        Environment.ExitCode = 1;
        return;
    }

    Environment.ExitCode = await ReconcileFlowAdministratorsCommand.RunAsync(app.Services);
    return;
}

if (migrateFiles)
{
    // 命令模式不启动 HTTP 监听；先使目标数据库处于当前迁移版本，再搬运内容。
    await using var scope = app.Services.CreateAsyncScope();
    var moved = await scope.ServiceProvider.GetRequiredService<IFileMigrationService>().MigrateAsync();
    Console.WriteLine($"已迁移 {moved} 个附件。");
    return;
}

if (backupNow)
{
    var target = await app.Services.GetRequiredService<SqliteBackupService>().CreateAsync("daily");
    Console.WriteLine($"已备份到 {target}。");
    return;
}

app.Run();

/// <summary>
/// 程序入口。声明为 public partial，供检查程序用 WebApplicationFactory 在进程内启动服务。
/// </summary>
public partial class Program;
