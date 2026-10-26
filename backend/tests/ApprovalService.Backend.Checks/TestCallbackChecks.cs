using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Administration;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 通过真实管理 HTTP、SQLite 和假出站接收端验证接入测试回调。
/// </summary>
internal static class TestCallbackChecks
{
    /// <summary>
    /// 运行管理授权、持久入队、签名投递及审计场景。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <param name="check">结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["ApprovalService:Identity:SigningKey"] = Convert.ToBase64String(new byte[64]),
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            };
            await using var server = new ServerFactory(backendRoot, directory, settings);
            using var client = server.CreateClient();
            using (var scope = server.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                var protector = scope.ServiceProvider.GetRequiredService<IIntegrationSecretProtector>();
                await using var db = await dbFactory.CreateDbContextAsync();
                db.People.AddRange(
                    new PersonEntity { Id = "org-admin", EmployeeNumber = "T100", Name = "组织管理员" },
                    new PersonEntity { Id = "app-admin", EmployeeNumber = "T200", Name = "应用管理员" },
                    new PersonEntity { Id = "ordinary", EmployeeNumber = "T300", Name = "普通人员" });
                db.OrganizationRoles.Add(new OrganizationRoleEntity
                {
                    Code = "organization-admin",
                    Name = "组织管理员",
                });
                db.PersonRoles.Add(new PersonRoleEntity
                {
                    PersonId = "org-admin",
                    RoleCode = "organization-admin",
                });
                db.ClientApps.AddRange(
                    new ClientAppEntity
                    {
                        Code = "sample",
                        Name = "有回调应用",
                        ApiKeyHash = new string('a', 64),
                        SigningSecretCiphertext = protector.Protect("test-secret"),
                        AdministratorPersonIdsJson = "[\"app-admin\"]",
                        CallbackUrl = "https://business.invalid/callback",
                    },
                    new ClientAppEntity
                    {
                        Code = "no-url",
                        Name = "未配置回调的应用",
                        ApiKeyHash = new string('b', 64),
                        SigningSecretCiphertext = protector.Protect("other-secret"),
                    });
                await db.SaveChangesAsync();
                var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
                await ProvisionAsync(identity, "org-admin", "admin-password");
                await ProvisionAsync(identity, "app-admin", "app-password");
                await ProvisionAsync(identity, "ordinary", "user-password");
            }

            var path = ApiRoutes.AdminAppTestCallback.Replace("{code}", "sample", StringComparison.Ordinal);
            using (var anonymous = await client.PostAsJsonAsync(path,
                new AdminTestCallbackRequest { Reason = "接入验签" }))
            {
                check("匿名调用测试回调接口被拒绝", anonymous.StatusCode == HttpStatusCode.Unauthorized);
            }

            var ordinaryToken = await LoginAsync(client, "T300", "user-password");
            var appToken = await LoginAsync(client, "T200", "app-password");
            var adminToken = await LoginAsync(client, "T100", "admin-password");
            using (var forbidden = await PostAsync(client, path, ordinaryToken, "接入验签"))
            {
                check("普通本地人员不能排入测试回调", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var invalid = await PostAsync(client, path, appToken, ""))
            {
                check("测试回调必须填写原因", invalid.StatusCode == HttpStatusCode.BadRequest);
            }

            var otherPath = ApiRoutes.AdminAppTestCallback.Replace("{code}", "no-url", StringComparison.Ordinal);
            using (var forbidden = await PostAsync(client, otherPath, appToken, "接入验签"))
            {
                check("应用管理员不能测试其他应用", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var invalid = await PostAsync(client, otherPath, adminToken, "接入验签"))
            {
                check("未登记回调地址时拒绝排队", invalid.StatusCode == HttpStatusCode.BadRequest);
            }

            AdminTestCallbackResponse? first;
            using (var accepted = await PostAsync(client, path, appToken, "接入验签"))
            {
                first = await accepted.Content.ReadFromJsonAsync<AdminTestCallbackResponse>();
                check("本应用管理员可发独立的持久测试事件", accepted.IsSuccessStatusCode &&
                    first?.EventId.StartsWith(CallbackEventCodes.TestEventIdPrefix,
                        StringComparison.Ordinal) == true && first.Status == "queued");
            }

            AdminTestCallbackResponse? second;
            using (var accepted = await PostAsync(client, path, adminToken, "管理员复测"))
            {
                second = await accepted.Content.ReadFromJsonAsync<AdminTestCallbackResponse>();
                check("组织管理员仍可发测试事件且每次 ID 不同", accepted.IsSuccessStatusCode &&
                    second?.EventId != first?.EventId);
            }

            using (var scope = server.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var items = await db.OutboxItems.AsNoTracking().Where(item =>
                    item.EventId == first!.EventId || item.EventId == second!.EventId).ToArrayAsync();
                var audits = await db.OrganizationChanges.AsNoTracking().Where(item =>
                    item.EntityKind == "callback" && item.Kind == "testQueued").ToArrayAsync();
                check("测试事件只排回调待发项，不建立审批单或业务动作", items.Length == 2 &&
                    items.All(item => item.Kind == "callback") &&
                    await db.Instances.CountAsync() == 0 && await db.ApprovalTasks.CountAsync() == 0);
                check("入队与管理员原因同事务留痕且不保存签名密钥", audits.Length == 2 &&
                    audits.Any(item => item.ActorId == "app-admin" && item.EntityId == first!.EventId) &&
                    audits.All(item => item.AfterJson is { } json && !json.Contains("test-secret",
                        StringComparison.Ordinal)));
                using var payload = JsonDocument.Parse(items.Single(item => item.EventId == first!.EventId).PayloadJson);
                var root = payload.RootElement;
                check("合成正文固定空业务单号、空数据和 test 状态", root.GetProperty("event").GetString() ==
                    CallbackEventCodes.IntegrationTest && root.GetProperty("instanceId").GetString() == first!.EventId &&
                    root.GetProperty("businessKey").GetString() == string.Empty &&
                    root.GetProperty("status").GetString() == CallbackNotification.TestStatus &&
                    root.GetProperty("data").EnumerateObject().Count() == 0 &&
                    root.GetProperty("clientAppCode").GetString() == "sample");

                var summaries = await scope.ServiceProvider.GetRequiredService<IAdminConsoleRepository>()
                    .ListOutboxAsync(200);
                check("管理队列可看到无真实审批单的测试回调及所属应用",
                    summaries.Any(item => item.EventId == first!.EventId && item.ClientAppCode == "sample"));

                var fake = new FakeCallbackReceiver();
                using var outboundClient = new HttpClient(fake);
                var handler = new CallbackOutboxHandler(dbFactory,
                    scope.ServiceProvider.GetRequiredService<IIntegrationSecretProtector>(),
                    new SignedHttpOutboundAdapter(outboundClient), TimeProvider.System);
                var outbox = items.Single(item => item.EventId == first!.EventId);
                outbox.Attempts = 1;
                fake.Fail = true;
                var failed = false;
                try
                {
                    await handler.DeliverAsync(outbox, CancellationToken.None);
                }
                catch (HttpRequestException)
                {
                    failed = true;
                }

                fake.Fail = false;
                outbox.Attempts = 2;
                await handler.DeliverAsync(outbox, CancellationToken.None);
                check("失败重试保持同一原始正文、请求 ID 和 HMAC 签名", failed && fake.Bodies.Count == 2 &&
                    fake.Bodies.All(body => body == outbox.PayloadJson) &&
                    fake.RequestIds.All(id => id == first!.EventId) &&
                    fake.Signatures.All(signature => signature ==
                        SignedHttpOutboundAdapter.Sign(outbox.PayloadJson, "test-secret")));
                await using var logsDb = await dbFactory.CreateDbContextAsync();
                var logs = await logsDb.OutboundSendLogs.AsNoTracking()
                    .Where(item => item.EventId == first!.EventId).ToArrayAsync();
                check("测试回调失败和成功均留下不含正文的发送日志", logs.Any(item =>
                    !item.Succeeded && item.HttpStatus == 503) && logs.Any(item =>
                    item.Succeeded && item.HttpStatus == 200));
                await logsDb.ClientApps.Where(item => item.Code == "sample")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.CallbackUrl, (string?)null));
                var missingUrl = false;
                try
                {
                    await handler.DeliverAsync(outbox, CancellationToken.None);
                }
                catch (UserFriendlyException error) when (error.Code == ErrorCodes.ValidationFailed)
                {
                    missingUrl = true;
                }

                check("排队后回调地址被清除时保留测试事件供重试", missingUrl &&
                    await logsDb.OutboundSendLogs.AsNoTracking().AnyAsync(item =>
                        item.EventId == first!.EventId && item.ErrorCode == "CALLBACK_URL_MISSING"));
            }

            using (var scope = server.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.ClientApps.Where(item => item.Code == "sample")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.CallbackUrl, "https://business.invalid/callback")
                        .SetProperty(item => item.AdministratorPersonIdsJson, "[]"));
            }

            using (var forbidden = await PostAsync(client, path, appToken, "撤权后复测"))
            {
                check("管理员名单撤权后旧本地会话立即失去测试权限", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var scope = server.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.ClientApps.Where(item => item.Code == "sample")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, false));
            }

            using (var forbidden = await PostAsync(client, path, adminToken, "停用后测试"))
            {
                check("应用停用后组织管理员也不能新增测试回调", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 为在职人员开通并完成首次改密。
    /// </summary>
    /// <param name="identity">身份应用服务。</param>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="password">日常登录密码。</param>
    /// <returns>异步操作。</returns>
    private static async Task ProvisionAsync(IIdentityAppService identity, string personId, string password)
    {
        var initial = "initial-" + personId + "-password";
        await identity.ProvisionAccountAsync(personId, initial);
        await identity.ChangePasswordAsync(personId, initial, password);
    }

    /// <summary>
    /// 调用真实密码登录接口。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="employeeNumber">工号。</param>
    /// <param name="password">密码。</param>
    /// <returns>已签发访问令牌。</returns>
    private static async Task<string> LoginAsync(HttpClient client, string employeeNumber, string password)
    {
        using var response = await client.PostAsJsonAsync(ApiRoutes.IdentityLogin,
            new IdentityLoginRequest(employeeNumber, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdentityTokensResponse>())!.AccessToken;
    }

    /// <summary>
    /// 带本地 JWT 和原因发起测试回调。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="path">目标应用测试路径。</param>
    /// <param name="token">访问令牌。</param>
    /// <param name="reason">测试原因。</param>
    /// <returns>未释放的响应。</returns>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string token,
        string reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new AdminTestCallbackRequest { Reason = reason });
        return await client.SendAsync(request);
    }

    /// <summary>
    /// 只记录签名、正文与请求 ID 的假接收端。
    /// </summary>
    private sealed class FakeCallbackReceiver : HttpMessageHandler
    {
        /// <summary>
        /// 下一次是否返回失败。
        /// </summary>
        public bool Fail
        {
            get; set;
        }

        /// <summary>
        /// 收到的原始正文。
        /// </summary>
        public List<string> Bodies { get; } = [];

        /// <summary>
        /// 收到的稳定请求 ID。
        /// </summary>
        public List<string?> RequestIds { get; } = [];

        /// <summary>
        /// 收到的 HMAC 签名。
        /// </summary>
        public List<string?> Signatures { get; } = [];

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            RequestIds.Add(request.Headers.GetValues(HeaderNames.RequestId).SingleOrDefault());
            Signatures.Add(request.Headers.GetValues(HeaderNames.Signature).SingleOrDefault());
            return new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty),
            };
        }
    }
}
