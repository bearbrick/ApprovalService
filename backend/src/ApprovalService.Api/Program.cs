using ApprovalService.Api.Endpoints;
using ApprovalService.Api.Endpoints.Files;
using ApprovalService.Api.Hosting;
using ApprovalService.Application;
using ApprovalService.Application.Files;
using ApprovalService.Contracts;
using ApprovalService.Infrastructure;

var migrateFiles = args.Contains("--migrate-files", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--migrate-files").ToArray());

// 三层各一个注册入口（docs/architecture.md）；接口的 JSON 写法和 Contracts 的约定保持一致。
builder.Services.AddApprovalApplication();
builder.Services.AddApprovalInfrastructure(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => ApprovalJson.Configure(options.SerializerOptions));

var app = builder.Build();

// 异常处理放最外层；请求 ID 和链路 ID 紧跟其后，出错响应里也能带上。
app.UseExceptionHandler(errorApp => errorApp.Run(ApiErrors.WriteUnhandledAsync));
app.UseMiddleware<RequestTraceMiddleware>();
app.UseStatusCodePages(context => ApiErrors.WriteStatusAsync(context.HttpContext));

app.MapSystemEndpoints();
app.MapFileEndpoints();

if (migrateFiles)
{
    // 命令模式不启动 HTTP 监听；先使目标数据库处于当前迁移版本，再搬运内容。
    await using var scope = app.Services.CreateAsyncScope();
    var moved = await scope.ServiceProvider.GetRequiredService<IFileMigrationService>().MigrateAsync();
    Console.WriteLine($"已迁移 {moved} 个附件。");
    return;
}

app.Run();

/// <summary>
/// 程序入口。声明为 public partial，供检查程序用 WebApplicationFactory 在进程内启动服务。
/// </summary>
public partial class Program;
