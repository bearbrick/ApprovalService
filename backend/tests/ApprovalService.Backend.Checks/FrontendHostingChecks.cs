using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用真实服务、静态构建目录和 JWT 检查审批中心页面的内嵌边界。
/// </summary>
internal static class FrontendHostingChecks
{
    /// <summary>
    /// 核对同源页面、静态绕过路径、应用隔离和嵌入上下文授权。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步检查。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var dataDirectory = TempDataDirectory.Create();
        try
        {
            var dist = Path.Combine(dataDirectory, "frontend-dist");
            Directory.CreateDirectory(Path.Combine(dist, "assets"));
            await File.WriteAllTextAsync(Path.Combine(dist, "index.html"), "<!doctype html><title>审批中心</title>");
            await File.WriteAllTextAsync(Path.Combine(dist, "direct.html"), "<!doctype html><title>直接地址</title>");
            await File.WriteAllTextAsync(Path.Combine(dist, "assets", "app.js"), "console.log('ready')");
            await File.WriteAllTextAsync(Path.Combine(dist, "assets", "side.html"), "<!doctype html><title>资源中的 HTML</title>");
            var settings = new Dictionary<string, string?>
            {
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
                ["ApprovalService:Frontend:DistPath"] = dist,
            };
            await using var server = new ServerFactory(backendRoot, dataDirectory, settings);
            using var http = server.CreateClient();
            using var ready = await http.GetAsync(ApiRoutes.HealthReady);
            var tokens = await CenterChecksFixture.SeedAsync(server.Services);
            using (var seedScope = server.Services.CreateScope())
            {
                await seedScope.ServiceProvider.GetRequiredService<ClientAppAppService>().CreateAsync(
                    new CreateClientAppInput { Code = "three", Name = "未登记来源应用" });
            }
            var factory = server.Services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.ClientApps.Single(item => item.Code == "one").AllowedEmbedOriginsJson =
                    JsonSerializer.Serialize(new[] { "https://business.example", "https://branch.example:8443" });
                db.ClientApps.Single(item => item.Code == "two").AllowedEmbedOriginsJson =
                    JsonSerializer.Serialize(new[] { "https://other.example" });
                await db.SaveChangesAsync();
            }

            using var home = await http.GetAsync(CenterRoutes.Home);
            using var direct = await http.GetAsync("/index.html");
            using var extra = await http.GetAsync("/direct.html");
            using var asset = await http.GetAsync("/assets/app.js");
            using var assetHtml = await http.GetAsync("/assets/side.html");
            using var unknown = await http.GetAsync("/unmatched-page");
            check("普通页面、直接静态 HTML 与资源地址均不能绕过默认禁嵌入策略",
                home.StatusCode == HttpStatusCode.OK && direct.StatusCode == HttpStatusCode.OK &&
                extra.StatusCode == HttpStatusCode.OK && asset.StatusCode == HttpStatusCode.OK &&
                assetHtml.StatusCode == HttpStatusCode.OK && unknown.StatusCode == HttpStatusCode.NotFound &&
                Csp(home) == "frame-ancestors 'none'" && Csp(direct) == "frame-ancestors 'none'" &&
                Csp(extra) == "frame-ancestors 'none'" && Csp(asset) == "frame-ancestors 'none'" &&
                Csp(assetHtml) == "frame-ancestors 'none'" && Csp(unknown) == "frame-ancestors 'none'" &&
                home.Headers.CacheControl?.NoStore == true && direct.Headers.CacheControl?.NoStore == true);

            using var first = await http.GetAsync("/embed/one");
            using var second = await http.GetAsync("/embed/two");
            using var empty = await http.GetAsync("/embed/three");
            using var missing = await http.GetAsync("/embed/missing");
            check("应用专属 HTML 只允许自身登记来源，未知应用始终拒绝内嵌",
                first.StatusCode == HttpStatusCode.OK && second.StatusCode == HttpStatusCode.OK &&
                Csp(first) == "frame-ancestors 'self' https://business.example https://branch.example:8443" &&
                Csp(second) == "frame-ancestors 'self' https://other.example" &&
                empty.StatusCode == HttpStatusCode.OK && Csp(empty) == "frame-ancestors 'self'" &&
                first.Headers.CacheControl?.NoStore == true && missing.StatusCode == HttpStatusCode.NotFound &&
                Csp(missing) == "frame-ancestors 'none'");

            using var anonymous = await http.GetAsync(CenterRoutes.EmbedContext);
            using var local = await AuthorizedAsync(http, CenterRoutes.EmbedContext, tokens.Local);
            using var embedded = await AuthorizedAsync(http, CenterRoutes.EmbedContext + "?clientAppCode=two", tokens.Embed);
            var context = await embedded.Content.ReadFromJsonAsync<CenterEmbedContextResponse>();
            check("嵌入上下文仅接受真实嵌入 JWT，外部查询参数不能改写应用和允许来源",
                anonymous.StatusCode == HttpStatusCode.Unauthorized && local.StatusCode == HttpStatusCode.Forbidden &&
                embedded.StatusCode == HttpStatusCode.OK && context is not null &&
                context.ClientAppCode == "one" &&
                context.AllowedOrigins.SequenceEqual(new[] { "https://business.example", "https://branch.example:8443" }) &&
                embedded.Headers.CacheControl?.NoStore == true);

            await using (var db = await factory.CreateDbContextAsync())
            {
                db.ClientApps.Single(item => item.Code == "one").AllowedEmbedOriginsJson =
                    "[\"https://business.example; script-src *\"]";
                await db.SaveChangesAsync();
            }
            using var corrupted = await http.GetAsync("/embed/one");
            check("损坏的持久化来源不能注入 CSP 指令",
                corrupted.StatusCode == HttpStatusCode.BadRequest && Csp(corrupted) == "frame-ancestors 'none'");

            using var scope = server.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ClientAppAppService>().SetEnabledAsync("two", false);
            using var disabled = await http.GetAsync("/embed/two");
            check("已停用接入应用不能继续提供可嵌入页面",
                disabled.StatusCode == HttpStatusCode.NotFound && Csp(disabled) == "frame-ancestors 'none'");

            await using (var production = new ProductionFrontendFactory(backendRoot,
                Path.Combine(dataDirectory, "production-data"), dist))
            {
                using var published = production.CreateClient();
                using var publishedHome = await published.GetAsync(CenterRoutes.Home);
                using var publishedIndex = await published.GetAsync("/index.html");
                using var publishedAsset = await published.GetAsync("/assets/app.js");
                check("生产环境同源提供页面和资源，直访构建入口仍拒绝内嵌",
                    publishedHome.StatusCode == HttpStatusCode.OK &&
                    publishedIndex.StatusCode == HttpStatusCode.OK &&
                    publishedAsset.StatusCode == HttpStatusCode.OK &&
                    Csp(publishedHome) == "frame-ancestors 'none'" &&
                    Csp(publishedIndex) == "frame-ancestors 'none'" &&
                    Csp(publishedAsset) == "frame-ancestors 'none'");
            }

            await VerifyMissingDistAsync(backendRoot, dataDirectory, check);
        }
        finally
        {
            TempDataDirectory.Delete(dataDirectory);
        }
    }

    /// <summary>
    /// 使用真实 Bearer 访问中心接口。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="path">目标地址。</param>
    /// <param name="token">已签发的访问令牌。</param>
    /// <returns>HTTP 响应。</returns>
    private static async Task<HttpResponseMessage> AuthorizedAsync(HttpClient http, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    /// <summary>
    /// 提取安全响应头的单个值。
    /// </summary>
    /// <param name="response">HTTP 响应。</param>
    /// <returns>策略正文；缺失时为空。</returns>
    private static string Csp(HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.ContentSecurityPolicy, out var values)
            ? values.Single() : string.Empty;

    /// <summary>
    /// 显式配置不存在的前端目录必须在服务启动时失败，避免发布时静默退化。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="dataDirectory">检查数据目录。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步检查。</returns>
    private static async Task VerifyMissingDistAsync(string backendRoot, string dataDirectory,
        Action<string, bool> check)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            ["ApprovalService:Frontend:DistPath"] = Path.Combine(dataDirectory, "missing-dist"),
        };
        await using var server = new ServerFactory(backendRoot, dataDirectory, settings);
        try
        {
            using var http = server.CreateClient();
            using var response = await http.GetAsync(CenterRoutes.Home);
            check("配置缺失的发布目录时不得静默提供不受保护的入口", false);
        }
        catch (InvalidOperationException error)
        {
            check("配置缺失的发布目录时启动明确失败", error.ToString().Contains("index.html", StringComparison.Ordinal));
        }
    }
}

/// <summary>
/// 以生产环境启动真实 API，验证已发布前端构建物的托管策略。
/// </summary>
/// <param name="backendRoot">后端根目录。</param>
/// <param name="dataDirectory">独立数据目录。</param>
/// <param name="distDirectory">前端构建目录。</param>
internal sealed class ProductionFrontendFactory(string backendRoot, string dataDirectory, string distDirectory)
    : WebApplicationFactory<global::Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseEnvironment("Production")
        .UseContentRoot(Path.Combine(backendRoot, "src", "ApprovalService.Api"))
        .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ApprovalService:Data:Directory"] = dataDirectory,
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
                ["ApprovalService:Frontend:DistPath"] = distDirectory,
            }));
}
