using ApprovalService.Api.Endpoints;
using ApprovalService.Api.Hosting;
using ApprovalService.Application;
using ApprovalService.Contracts;
using ApprovalService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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

app.Run();

/// <summary>
/// 程序入口。声明为 public partial，供检查程序用 WebApplicationFactory 在进程内启动服务。
/// </summary>
public partial class Program;
