using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApprovalService.Application.Center;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用真实 SQLite、JWT 和 HTTP 检查审批中心只读授权边界。
/// </summary>
internal static class CenterQueryChecks
{
    /// <summary>
    /// 检查四列表、隐私范围、字段裁剪和只读接口行为。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">结果记录器。</param>
    /// <returns>异步检查。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            };
            await using var server = new ServerFactory(backendRoot, directory, settings);
            using var http = server.CreateClient();
            using var ready = await http.GetAsync(ApiRoutes.HealthReady);
            var tokens = await CenterChecksFixture.SeedAsync(server.Services);
            using var anonymous = await http.GetAsync(CenterRoutes.Instances);
            using var keyRequest = new HttpRequestMessage(HttpMethod.Get, CenterRoutes.Instances);
            keyRequest.Headers.Add(HeaderNames.ApiKey, tokens.ApiKey);
            using var keyOnly = await http.SendAsync(keyRequest);
            using var first = await GetAsync(http, CenterRoutes.Instances, tokens.First);
            check("中心查询拒绝匿名、仅 API Key 和首次未改密令牌", anonymous.StatusCode == HttpStatusCode.Unauthorized &&
                keyOnly.StatusCode == HttpStatusCode.Unauthorized && first.StatusCode == HttpStatusCode.Unauthorized);

            var local = await ListAsync(http, tokens.Local, "todo");
            var embedded = await ListAsync(http, tokens.Embed, "todo");
            check("本地待办跨应用可见且同单多任务去重，嵌入只含本应用并包含原审批人代理待办",
                local.Total == 3 && local.Items.Select(item => item.Id).Order().SequenceEqual(new[] { "a-todo", "b-cross", "j-delegated" }) &&
                embedded.Total == 2 && embedded.Items.All(item => item.ClientAppCode == "one") &&
                local.Items.Single(item => item.Id == "a-todo").PendingTaskIds.Count == 2);
            check("加急单排在前，摘要隐藏顶层和明细成本字段",
                local.Items[0].Id == "a-todo" && local.Items[0].Summary.GetProperty("amount").GetInt32() == 12 &&
                !local.Items[0].Summary.TryGetProperty("secret", out _) &&
                !local.Items[0].Summary.GetProperty("lines")[0].TryGetProperty("cost", out _));
            check("中心列表返回真实人员、来源应用和当前环节名称",
                local.Items[0].InitiatorName == "测试人员" && local.Items[0].SourceName == "应用一" &&
                !string.IsNullOrWhiteSpace(local.Items[0].CurrentStep));
            var done = await ListAsync(http, tokens.Local, "done");
            var copy = await ListAsync(http, tokens.Local, "cc");
            var started = await ListAsync(http, tokens.Local, "started");
            check("已办使用真实处理投影，抄送含无任务的已送达收件人，发起含实际代发",
                done.Total == 2 && done.Items.Select(item => item.Id).Order().SequenceEqual(new[] { "c-done", "k-history" }) &&
                copy.Items is [{ Id: "d-copy" }] && started.Total == 2 &&
                started.Items.Select(item => item.Id).Order().SequenceEqual(new[] { "e-started", "f-submitted" }));
            var paged = await ListAsync(http, tokens.Embed, "todo&take=1&skip=1");
            var filtered = await ListAsync(http, tokens.Local, "todo&flowCode=CENTER&status=running&search=B-b-cross&from=2026-10-23T00:00:00Z&to=2026-10-24T00:00:00Z");
            var noLeak = await ListAsync(http, tokens.Embed, "todo&search=B-b-cross");
            var forged = await ListAsync(http, tokens.Embed, "todo&personId=other&clientAppCode=two&scope=all");
            check("中心筛选和分页总数在授权后计算，外部查询参数不能扩张人员应用范围",
                paged.Total == 2 && paged.Items.Count == 1 && filtered.Items is [{ Id: "b-cross" }] &&
                noLeak.Total == 0 && noLeak.Items.Count == 0 && forged.Total == 2 && forged.Items.All(item => item.ClientAppCode == "one"));

            using var detailResponse = await GetAsync(http, Path("a-todo"), tokens.Local);
            var detailText = await detailResponse.Content.ReadAsStringAsync();
            var detail = await detailResponse.Content.ReadFromJsonAsync<CenterDetailResponse>();
            check("中心详情只返回已定义可见字段，明细及审计载荷不能绕过字段权限",
                detailResponse.StatusCode == HttpStatusCode.OK && detail is not null &&
                detail.Data.GetProperty("amount").GetInt32() == 12 && detail.Tasks.Count == 2 && detail.Events.Count == 1 &&
                !detailText.Contains("hidden-business-secret", StringComparison.Ordinal) &&
                !detailText.Contains("hidden-event-secret", StringComparison.Ordinal) &&
                !detailText.Contains("unregistered", StringComparison.Ordinal) &&
                !detailText.Contains("private-unsent-draft", StringComparison.Ordinal) &&
                !detailText.Contains("stateJson", StringComparison.Ordinal) &&
                detailResponse.Headers.CacheControl?.NoStore == true);
            foreach (var id in new[] { "g-forbidden", "h-queued", "i-cancelled", "missing" })
            {
                using var denied = await GetAsync(http, Path(id), tokens.Local);
                var error = await denied.Content.ReadFromJsonAsync<ApiErrorResponse>();
                check("中心无权或不存在详情统一不暴露单据：" + id,
                    denied.StatusCode == HttpStatusCode.NotFound && error?.Code == ErrorCodes.NotFound);
            }
            using var foreign = await GetAsync(http, Path("b-cross"), tokens.Embed);
            using var administrator = await GetAsync(http, Path("a-todo"), tokens.Stranger);
            using var delegated = await GetAsync(http, Path("j-delegated"), tokens.Local);
            using var historical = await GetAsync(http, Path("k-history"), tokens.Local);
            using var copied = await GetAsync(http, Path("d-copy"), tokens.Local);
            var historyText = await historical.Content.ReadAsStringAsync();
            check("历史参与者不能借未来未到达节点放宽隐藏字段", !historyText.Contains("hidden-business-secret", StringComparison.Ordinal));
            check("保密详情拒绝跨应用与无关管理员，但允许当前原审批人及历史处理抄送参与者",
                foreign.StatusCode == HttpStatusCode.NotFound && administrator.StatusCode == HttpStatusCode.NotFound &&
                delegated.StatusCode == HttpStatusCode.OK && historical.StatusCode == HttpStatusCode.OK && copied.StatusCode == HttpStatusCode.OK);
            foreach (var query in new[] { "section=unknown", "take=101", "skip=-1", "from=2026-10-24T00:00:00Z&to=2026-10-23T00:00:00Z" })
            {
                using var invalid = await GetAsync(http, CenterRoutes.Instances + "?" + query, tokens.Local);
                check("中心查询拒绝无效条件：" + query, invalid.StatusCode == HttpStatusCode.BadRequest);
            }
            using var write = new HttpRequestMessage(HttpMethod.Post, CenterRoutes.Instances);
            write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Local);
            using var writeResponse = await http.SendAsync(write);
            using var serviceScope = server.Services.CreateScope();
            var factory = serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("中心入口只有查询且不修改状态、审批数据和审计", writeResponse.StatusCode == HttpStatusCode.MethodNotAllowed &&
                    await db.Instances.CountAsync() == 11 && await db.Instances.AllAsync(item => item.Version == 0) &&
                    await db.OperationEvents.CountAsync() == 12);
            }
            await VerifyRepositoryAsync(serviceScope.ServiceProvider, check);
            await serviceScope.ServiceProvider.GetRequiredService<IIdentityAppService>().RevokeAllAsync("viewer");
            using var revoked = await GetAsync(http, CenterRoutes.Instances, tokens.Local);
            check("中心每次访问重新验证令牌版本，已作废令牌不能读取", revoked.StatusCode == HttpStatusCode.Unauthorized);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 直接调用仓储证明授权在数据查询边界而非仅在 HTTP 层执行。
    /// </summary>
    /// <param name="services">当前服务作用域。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步检查。</returns>
    private static async Task VerifyRepositoryAsync(IServiceProvider services, Action<string, bool> check)
    {
        var repository = services.GetRequiredService<ICenterQueryRepository>();
        var scope = new CenterScope { PersonId = "viewer", ClientAppCode = "one" };
        var foreign = await repository.FindAsync(scope, "b-cross");
        var unrelated = await repository.FindAsync(new CenterScope { PersonId = "stranger" }, "a-todo");
        var page = await repository.ListAsync(scope, new CenterQuery());
        check("仓储独立执行人员与嵌入应用过滤，越权详情不会加载",
            foreign is null && unrelated is null && page.Total == 2 && page.Items.All(item => item.Instance.ClientAppCode == "one"));
        try
        {
            await services.GetRequiredService<CenterQueryAppService>().ListAsync(new CenterScope(), new CenterQuery());
            check("中心用例拒绝空人员范围", false);
        }
        catch (UserFriendlyException exception)
        {
            check("中心用例拒绝空人员范围", exception.Code == ErrorCodes.Unauthorized);
        }
    }

    /// <summary>
    /// 通过真实 HTTP 查询列表并反序列化响应。
    /// </summary>
    /// <param name="http">宿主客户端。</param>
    /// <param name="token">访问令牌。</param>
    /// <param name="sectionAndQuery">分组及附加测试查询。</param>
    /// <returns>成功返回的列表页。</returns>
    private static async Task<CenterListResponse> ListAsync(HttpClient http, string token, string sectionAndQuery)
    {
        using var response = await GetAsync(http, CenterRoutes.Instances + "?section=" + sectionAndQuery, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CenterListResponse>() ?? throw new InvalidOperationException("列表为空响应。");
    }

    /// <summary>
    /// 使用真实 JWT 读取指定中心路由。
    /// </summary>
    /// <param name="http">宿主客户端。</param>
    /// <param name="path">接口地址。</param>
    /// <param name="token">访问令牌。</param>
    /// <returns>调用方负责释放的响应。</returns>
    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    /// <summary>
    /// 从权威路由生成详情地址。
    /// </summary>
    /// <param name="id">审批单标识。</param>
    /// <returns>可请求的相对地址。</returns>
    private static string Path(string id) => CenterRoutes.Instance.Replace("{id}", Uri.EscapeDataString(id), StringComparison.Ordinal);
}
