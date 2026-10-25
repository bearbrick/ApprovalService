using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用真实 JWT、HTTP 和 SQLite 验证常用意见的个人隔离与嵌入只读边界。
/// </summary>
internal static class CenterOpinionChecks
{
    /// <summary>
    /// 执行常用意见完整生命周期场景。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">结果记录器。</param>
    /// <returns>检查完成。</returns>
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
            var tokens = await CenterChecksFixture.SeedAsync(server.Services);

            using var anonymous = await http.GetAsync(CenterRoutes.Opinions);
            using var keyOnly = new HttpRequestMessage(HttpMethod.Get, CenterRoutes.Opinions);
            keyOnly.Headers.Add(HeaderNames.ApiKey, tokens.ApiKey);
            using var keyResponse = await http.SendAsync(keyOnly);
            check("常用意见拒绝匿名与仅 API Key 的请求",
                anonymous.StatusCode == HttpStatusCode.Unauthorized && keyResponse.StatusCode == HttpStatusCode.Unauthorized);

            using var createdResponse = await SendAsync(http, HttpMethod.Post, CenterRoutes.Opinions, tokens.Local,
                new CenterOpinionRequest { Content = "  已核对  " });
            var created = await createdResponse.Content.ReadFromJsonAsync<CenterOpinionResponse>();
            check("本地 JWT 新增意见并去除首尾空白，响应不暴露人员标识",
                createdResponse.StatusCode == HttpStatusCode.OK && created?.Content == "已核对" &&
                !string.IsNullOrWhiteSpace(created.Id) &&
                !(await createdResponse.Content.ReadAsStringAsync()).Contains("personId", StringComparison.OrdinalIgnoreCase));
            if (created is null)
            {
                return;
            }

            using var embeddedList = await SendAsync(http, HttpMethod.Get, CenterRoutes.Opinions, tokens.Embed);
            var embeddedItems = await embeddedList.Content.ReadFromJsonAsync<List<CenterOpinionResponse>>();
            using var otherList = await SendAsync(http, HttpMethod.Get, CenterRoutes.Opinions, tokens.Stranger);
            var otherItems = await otherList.Content.ReadFromJsonAsync<List<CenterOpinionResponse>>();
            check("同一人员嵌入会话可只读使用，本人意见不泄露给其他人员",
                embeddedList.StatusCode == HttpStatusCode.OK && embeddedItems is [{ Content: "已核对" }] &&
                otherList.StatusCode == HttpStatusCode.OK && otherItems is { Count: 0 });

            using var embeddedWrite = await SendAsync(http, HttpMethod.Post, CenterRoutes.Opinions, tokens.Embed,
                new CenterOpinionRequest { Content = "越权" });
            using var embeddedUpdate = await SendAsync(http, HttpMethod.Put, Path(created.Id), tokens.Embed,
                new CenterOpinionRequest { Content = "越权" });
            using var embeddedDelete = await SendAsync(http, HttpMethod.Delete, Path(created.Id), tokens.Embed);
            check("嵌入会话新增、修改和删除均被拒绝",
                embeddedWrite.StatusCode == HttpStatusCode.Forbidden &&
                embeddedUpdate.StatusCode == HttpStatusCode.Forbidden && embeddedDelete.StatusCode == HttpStatusCode.Forbidden);

            using var crossUpdate = await SendAsync(http, HttpMethod.Put, Path(created.Id), tokens.Stranger,
                new CenterOpinionRequest { Content = "越权" });
            using var crossDelete = await SendAsync(http, HttpMethod.Delete, Path(created.Id), tokens.Stranger);
            check("其他人员修改或删除一律按不存在处理",
                crossUpdate.StatusCode == HttpStatusCode.NotFound && crossDelete.StatusCode == HttpStatusCode.NotFound);

            using var blank = await SendAsync(http, HttpMethod.Post, CenterRoutes.Opinions, tokens.Local,
                new CenterOpinionRequest { Content = "   " });
            using var overlong = await SendAsync(http, HttpMethod.Put, Path(created.Id), tokens.Local,
                new CenterOpinionRequest { Content = new string('字', 501) });
            check("空白和超过五百字的意见均在服务端拒绝",
                blank.StatusCode == HttpStatusCode.BadRequest && overlong.StatusCode == HttpStatusCode.BadRequest);

            using var changedResponse = await SendAsync(http, HttpMethod.Put, Path(created.Id), tokens.Local,
                new CenterOpinionRequest { Content = "  请修改  " });
            var changed = await changedResponse.Content.ReadFromJsonAsync<CenterOpinionResponse>();
            check("本人修改正文保留原意见标识", changedResponse.StatusCode == HttpStatusCode.OK &&
                changed?.Id == created.Id && changed.Content == "请修改");

            for (var index = 1; index < 30; index++)
            {
                using var response = await SendAsync(http, HttpMethod.Post, CenterRoutes.Opinions, tokens.Local,
                    new CenterOpinionRequest { Content = "常用意见" + index });
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new InvalidOperationException("常用意见在未满三十条时创建失败。");
                }
            }
            using var full = await SendAsync(http, HttpMethod.Post, CenterRoutes.Opinions, tokens.Local,
                new CenterOpinionRequest { Content = "第三十一条" });
            using var fullList = await SendAsync(http, HttpMethod.Get, CenterRoutes.Opinions, tokens.Local);
            var fullItems = await fullList.Content.ReadFromJsonAsync<List<CenterOpinionResponse>>();
            check("每人至多保存三十条且满额失败不产生额外记录",
                full.StatusCode == HttpStatusCode.Conflict && fullItems?.Count == 30);

            using var removed = await SendAsync(http, HttpMethod.Delete, Path(created.Id), tokens.Local);
            using var repeated = await SendAsync(http, HttpMethod.Delete, Path(created.Id), tokens.Local);
            using var afterDelete = await SendAsync(http, HttpMethod.Post, CenterRoutes.Opinions, tokens.Local,
                new CenterOpinionRequest { Content = "腾出名额" });
            check("删除本人意见后释放名额，重复删除按不存在处理",
                removed.StatusCode == HttpStatusCode.NoContent && repeated.StatusCode == HttpStatusCode.NotFound &&
                afterDelete.StatusCode == HttpStatusCode.OK);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 构造单条个人意见地址。
    /// </summary>
    /// <param name="id">意见标识。</param>
    /// <returns>可安全发送的路径。</returns>
    private static string Path(string id) => CenterRoutes.Opinions + "/" + Uri.EscapeDataString(id);

    /// <summary>
    /// 将令牌只放入当前请求头，不在 URI 中传递。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">接口路径。</param>
    /// <param name="token">已签发人员令牌。</param>
    /// <param name="input">可选请求体。</param>
    /// <returns>服务端响应，由调用方释放。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path,
        string token, CenterOpinionRequest? input = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (input is not null)
        {
            request.Content = JsonContent.Create(input);
        }
        return await http.SendAsync(request);
    }
}
