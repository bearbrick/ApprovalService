using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ApprovalService.Application.Printing;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Persistence;
using ApprovalService.Infrastructure.Printing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PdfSharp.Pdf.IO;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 打印权限、字段裁剪、字体校验、计数和 PDF 分页的真实服务检查。
/// </summary>
internal static class PrintingChecks
{
    /// <summary>
    /// 执行真实 JWT 与 SQLite 场景；渲染字体仅由测试环境提供，不随产品分发。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>检查完成。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var rejected = false;
        try
        {
            PrintFontValidator.Validate(new byte[128]);
        }
        catch (UserFriendlyException) { rejected = true; }
        check("打印字体拒绝伪造文件头", rejected);
        var directory = TempDataDirectory.Create();
        try
        {
            await using var server = new ServerFactory(backendRoot, directory, new Dictionary<string, string?>
            {
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
                ["ApprovalService:Printing:PublicBaseUrl"] = "https://approval.example.test",
            });
            var printer = new RecordingPrinter();
            await using var host = server.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IApprovalPrinter>();
                services.AddSingleton<IApprovalPrinter>(printer);
            }));
            using var http = host.CreateClient();
            var tokens = await CenterChecksFixture.SeedAsync(host.Services);
            using var anonymous = await SendAsync(http, PrintPath("a-todo"));
            using var forbidden = await SendAsync(http, PrintPath("g-forbidden"), tokens.Local);
            using var cross = await SendAsync(http, PrintPath("b-cross"), tokens.Embed);
            using var stranger = await SendAsync(http, PrintPath("a-todo"), tokens.Stranger);
            using var missingFont = await SendAsync(http, PrintPath("a-todo"), tokens.Local);
            check("打印拒绝匿名、无关管理员、不可见审批单及跨应用嵌入请求",
                anonymous.StatusCode == HttpStatusCode.Unauthorized && forbidden.StatusCode == HttpStatusCode.NotFound &&
                cross.StatusCode == HttpStatusCode.NotFound && stranger.StatusCode == HttpStatusCode.NotFound);
            check("尚未配置字体时返回可修正冲突而非空白 PDF", missingFont.StatusCode == HttpStatusCode.Conflict);
            using var deniedFont = await SendAsync(http, PrintingRoutes.Font, tokens.Local);
            using var embeddedFont = await SendAsync(http, PrintingRoutes.Font, tokens.Embed);
            check("字体管理只允许本地组织管理员", deniedFont.StatusCode == HttpStatusCode.Forbidden &&
                embeddedFont.StatusCode == HttpStatusCode.Forbidden);
            var fontPath = Environment.GetEnvironmentVariable("APPROVAL_TEST_FONT");
            if (string.IsNullOrWhiteSpace(fontPath))
            {
                Console.WriteLine("提示：APPROVAL_TEST_FONT 未设置，跳过依赖单位授权字体的 PDF 渲染场景。");
                return;
            }
            var bytes = await File.ReadAllBytesAsync(fontPath);
            using var upload = await SendAsync(http, PrintingRoutes.Font + "?fileName=test.ttf&licenseConfirmed=true", tokens.Stranger, bytes);
            check("管理员可上传经过实际解析和中文字形校验的字体", upload.IsSuccessStatusCode);
            using var first = await SendAsync(http, PrintPath("a-todo"), tokens.Local);
            if (!first.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await first.Content.ReadAsStringAsync());
            }
            var pdf = await first.Content.ReadAsByteArrayAsync();
            using var parsed = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
            check("已授权人员得到带 PDF 内容类型和禁止缓存标记的有效文档", parsed.PageCount >= 1 &&
                first.Content.Headers.ContentType?.MediaType == "application/pdf" && first.Headers.CacheControl?.NoStore == true);
            check("打印只接收权限裁剪后的字段、明细列和已关联附件", printer.Last is { } input &&
                !input.Detail.Data.TryGetProperty("secret", out _) && !input.Detail.Data.GetProperty("lines")[0].TryGetProperty("cost", out _) &&
                input.Detail.Attachments.Select(file => file.Id).SequenceEqual(["file-visible", "file-row"]));
            using var second = await SendAsync(http, PrintPath("a-todo"), tokens.Embed);
            check("同应用嵌入会话可以打印且次数递增", second.IsSuccessStatusCode && printer.Last?.PrintNumber == 2);
            using var appRequest = new HttpRequestMessage(HttpMethod.Get, PrintPath("a-todo"));
            appRequest.Headers.Add(HeaderNames.ApiKey, tokens.ApiKey);
            using var appPdf = await http.SendAsync(appRequest);
            check("所属应用 API Key 可以归档且以应用名称记录打印人", appPdf.IsSuccessStatusCode &&
                printer.Last?.PrintNumber == 3 && printer.Last.PrintedBy == "接入应用：应用一");
            using var otherRequest = new HttpRequestMessage(HttpMethod.Get, PrintPath("b-cross"));
            otherRequest.Headers.Add(HeaderNames.ApiKey, tokens.ApiKey);
            using var otherPdf = await http.SendAsync(otherRequest);
            check("API Key 不能归档其他来源应用的审批单", otherPdf.StatusCode == HttpStatusCode.NotFound);
            using (var scope = host.Services.CreateScope())
            {
                var runtime = scope.ServiceProvider.GetRequiredService<IApprovalRuntimeRepository>();
                var chain = await runtime.LoadEventChainAsync("a-todo");
                check("每次成功生成追加完整哈希链打印审计", chain is not null &&
                    chain.Events.Count(item => item.Kind == PrintingRoutes.PrintedEvent) == 3 &&
                    chain.Events.Where(item => item.Kind == PrintingRoutes.PrintedEvent).All(item => item.Hash == OperationHash.Compute(item)) &&
                    chain.LastEventHash == chain.Events[^1].Hash && chain.Events[^1].PreviousHash == chain.Events[^2].Hash);
            }
            printer.AfterRender = () =>
            {
                using var scope = host.Services.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                using var db = factory.CreateDbContext();
                db.Instances.Where(item => item.Id == "a-todo").ExecuteUpdate(setters =>
                    setters.SetProperty(item => item.Version, item => item.Version + 1));
            };
            using var concurrent = await SendAsync(http, PrintPath("a-todo"), tokens.Local);
            printer.AfterRender = null;
            check("生成期间审批单版本变化时拒绝交付旧 PDF", concurrent.StatusCode == HttpStatusCode.Conflict);
            var longInput = printer.Last!;
            longInput.Detail.Data = JsonSerializer.SerializeToElement(new
            {
                lines = Enumerable.Range(1, 150).Select(index => new { qty = index, receipt = "长明细跨页验证" }),
            });
            longInput.Settings.ShowWatermark = true;
            var multipage = new PdfApprovalPrinter().Render(longInput);
            using var pages = PdfReader.Open(new MemoryStream(multipage), PdfDocumentOpenMode.Import);
            check("中文长明细可跨页生成并保留有效页树", pages.PageCount > 2);
            var evidence = Environment.GetEnvironmentVariable("APPROVAL_PRINT_EVIDENCE");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                Directory.CreateDirectory(evidence);
                await File.WriteAllBytesAsync(Path.Combine(evidence, "approval-print.pdf"), pdf);
                await File.WriteAllBytesAsync(Path.Combine(evidence, "approval-multipage.pdf"), multipage);
            }
        }
        finally { TempDataDirectory.Delete(directory); }
    }

    /// <summary>
    /// 取得打印路由。
    /// </summary>
    /// <param name="id">审批单 ID。</param>
    /// <returns>接口路径。</returns>
    private static string PrintPath(string id) => PrintingRoutes.Instance.Replace("{id}", id, StringComparison.Ordinal);

    /// <summary>
    /// 发送可选 JWT 的真实请求。
    /// </summary>
    /// <param name="http">客户端。</param>
    /// <param name="path">路径。</param>
    /// <param name="token">可选令牌。</param>
    /// <param name="bytes">存在时以 POST 上传原始字体。</param>
    /// <returns>调用方释放的响应。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, string path, string? token = null, byte[]? bytes = null)
    {
        using var request = new HttpRequestMessage(bytes is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (bytes is not null)
        {
            request.Content = new ByteArrayContent(bytes);
        }

        return await http.SendAsync(request);
    }
}

/// <summary>
/// 保留真实渲染输入以断言字段裁剪，不代替实际 PDF 生成。
/// </summary>
internal sealed class RecordingPrinter : IApprovalPrinter
{
    /// <summary>
    /// 最近一次已授权输入。
    /// </summary>
    public ApprovalPrintInput? Last
    {
        get; private set;
    }
    /// <summary>
    /// 在生成完成和提交之间模拟另一请求的版本修改。
    /// </summary>
    public Action? AfterRender
    {
        get; set;
    }
    /// <inheritdoc />
    public byte[] Render(ApprovalPrintInput input)
    {
        Last = input;
        var pdf = new PdfApprovalPrinter().Render(input);
        AfterRender?.Invoke();
        return pdf;
    }
    /// <inheritdoc />
    public void ValidateFont(byte[] bytes) => new PdfApprovalPrinter().ValidateFont(bytes);
}
