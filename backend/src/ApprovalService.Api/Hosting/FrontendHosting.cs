using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using Microsoft.Extensions.FileProviders;

namespace ApprovalService.Api.Hosting;

/// <summary>
/// 同源托管审批中心页面，并对每份可导航文档设置内嵌来源边界。
/// </summary>
internal static class FrontendHosting
{
    /// <summary>
    /// 在最外层为成功、异常和未知路径响应都登记默认禁嵌入策略。
    /// </summary>
    /// <param name="app">服务端应用。</param>
    /// <returns>原服务端应用。</returns>
    public static WebApplication UseFrontendDocumentSecurity(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            // 异常处理中间件可能清空已写的头；响应开始时再补默认值。
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
                {
                    context.Response.Headers[HeaderNames.ContentSecurityPolicy] = "frame-ancestors 'none'";
                }

                return Task.CompletedTask;
            });
            await next();
        });
        return app;
    }

    /// <summary>
    /// 在生产环境必须找到前端构建物；开发环境未构建时仍允许独立启动 API。
    /// </summary>
    /// <param name="app">服务端应用。</param>
    /// <returns>原服务端应用。</returns>
    public static WebApplication UseApprovalFrontend(this WebApplication app)
    {
        var configuredPath = app.Configuration["ApprovalService:Frontend:DistPath"];
        var directory = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(app.Environment.ContentRootPath, "wwwroot")
            : Path.GetFullPath(configuredPath, app.Environment.ContentRootPath);
        var indexPath = Path.Combine(directory, "index.html");
        if (!File.Exists(indexPath))
        {
            if (app.Environment.IsProduction() || !string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidOperationException("前端发布目录缺少 index.html，请先构建前端并配置 ApprovalService:Frontend:DistPath。");
            }

            return app;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(directory),
            OnPrepareResponse = context =>
            {
                // 直接访问 /index.html 等静态 HTML 仍只能得到默认禁嵌入策略。
                if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "no-store";
                }
            },
        });
        app.MapGet(CenterRoutes.Home, (HttpContext http) => Document(http, indexPath))
            .ExcludeFromDescription();
        app.MapGet(CenterRoutes.EmbedDocument, async (HttpContext http, string clientAppCode,
            ClientAppAppService apps, CancellationToken cancellationToken) =>
        {
            var origins = await apps.GetAllowedEmbedOriginsAsync(clientAppCode,
                app.Environment.IsDevelopment(), cancellationToken);
            // 只在此专用入口放宽父页面；静态入口和其他应用的配置始终不受影响。
            http.Response.Headers[HeaderNames.ContentSecurityPolicy] =
                "frame-ancestors 'self'" + (origins.Count == 0 ? string.Empty : " " + string.Join(' ', origins));
            return Document(http, indexPath);
        }).ExcludeFromDescription();
        return app;
    }

    /// <summary>
    /// 以同源、不可缓存的 HTML 响应返回前端入口。
    /// </summary>
    /// <param name="http">当前请求。</param>
    /// <param name="indexPath">已确认存在的构建入口。</param>
    /// <returns>HTML 文件响应。</returns>
    private static IResult Document(HttpContext http, string indexPath)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.File(indexPath, "text/html; charset=utf-8");
    }
}
