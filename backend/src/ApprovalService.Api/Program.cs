using System.Globalization;
using System.Security.Claims;
using ApprovalService.Api.Endpoints;
using ApprovalService.Api.Endpoints.OpenApi;
using ApprovalService.Api.Hosting;
using ApprovalService.Application;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Infrastructure;
using ApprovalService.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

var builder = WebApplication.CreateBuilder(args);

// 三层各一个注册入口（docs/architecture.md）；接口的 JSON 写法和 Contracts 的约定保持一致。
builder.Services.AddApprovalApplication();
builder.Services.AddApprovalInfrastructure(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => ApprovalJson.Configure(options.SerializerOptions));
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
                if (string.IsNullOrWhiteSpace(personId) ||
                    !long.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
                    !await context.HttpContext.RequestServices.GetRequiredService<IIdentityAppService>()
                        .ValidateAccessPrincipalAsync(personId, version, sessionKind, clientAppCode,
                            externalUserId, context.HttpContext.RequestAborted))
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

// 异常处理放最外层；请求 ID 和链路 ID 紧跟其后，出错响应里也能带上。
app.UseExceptionHandler(errorApp => errorApp.Run(ApiErrors.WriteUnhandledAsync));
app.UseMiddleware<RequestTraceMiddleware>();
app.UseStatusCodePages(context => ApiErrors.WriteStatusAsync(context.HttpContext));
app.UseAuthentication();
app.UseAuthorization();

app.MapSystemEndpoints();
app.MapOpenFlowEndpoints();
app.MapOpenInstanceQueryEndpoints();
app.MapOpenTaskEndpoints();
app.MapOpenStartEndpoints();
app.MapOpenInstanceCommandEndpoints();
app.MapOpenTaskActionEndpoints();
app.MapIdentityEndpoints();
app.MapOrganizationEndpoints();


app.Run();

/// <summary>
/// 程序入口。声明为 public partial，供检查程序用 WebApplicationFactory 在进程内启动服务。
/// </summary>
public partial class Program;
