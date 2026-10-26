using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Notifications;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Administration;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Notifications;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 真实 JWT、HTTP 和迁移后的 SQLite 日历管理场景。
/// </summary>
internal static class WorkCalendarAdminChecks
{
    /// <summary>
    /// 验证权限、校验、修订和审计。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>检查完成。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["ApprovalService:Identity:SigningKey"] = Convert.ToBase64String(new byte[64]),
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
                ["ApprovalService:Notifications:Calendar:WorkingStart"] = "08:30",
                ["ApprovalService:Notifications:Calendar:WorkingEnd"] = "17:30",
                ["ApprovalService:Notifications:Calendar:Overrides:2026-10-01"] = "false",
            };
            await using var server = new ServerFactory(backendRoot, directory, settings);
            using var http = server.CreateClient();
            using var scope = server.Services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.People.AddRange(new PersonEntity { Id = "calendar-admin", EmployeeNumber = "CA001", Name = "日历管理员" },
                    new PersonEntity { Id = "calendar-user", EmployeeNumber = "CU001", Name = "普通人员" });
                db.OrganizationRoles.Add(new OrganizationRoleEntity { Code = "organization-admin", Name = "组织管理员" });
                db.PersonRoles.Add(new PersonRoleEntity { PersonId = "calendar-admin", RoleCode = "organization-admin" });
                await db.SaveChangesAsync();
            }
            var app = await scope.ServiceProvider.GetRequiredService<ClientAppAppService>().CreateAsync(
                new CreateClientAppInput { Code = "calendar-app", Name = "日历测试应用" });
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.ExternalPersonBindings.Add(new ExternalPersonBindingEntity
                {
                    ClientAppCode = "calendar-app",
                    ExternalUserId = "external-admin",
                    PersonId = "calendar-admin",
                });
                await db.SaveChangesAsync();
            }
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
            foreach (var id in new[] { "calendar-admin", "calendar-user" })
            {
                await identity.ProvisionAccountAsync(id, "initial-calendar-password");
                await identity.ChangePasswordAsync(id, "initial-calendar-password", "normal-calendar-password");
            }
            using var login = await http.PostAsJsonAsync(ApiRoutes.IdentityLogin,
                new IdentityLoginRequest("CA001", "normal-calendar-password"));
            var admin = (await login.Content.ReadFromJsonAsync<IdentityTokensResponse>())!.AccessToken;
            using var userLogin = await http.PostAsJsonAsync(ApiRoutes.IdentityLogin,
                new IdentityLoginRequest("CU001", "normal-calendar-password"));
            var user = (await userLogin.Content.ReadFromJsonAsync<IdentityTokensResponse>())!.AccessToken;
            var embed = await identity.CreateEmbedTokenAsync("calendar-app", "external-admin", "calendar-admin", "#/center");
            var embedTokens = await identity.ExchangeEmbedTokenAsync(embed.Token);
            using var embeddedRead = await SendAsync(http, HttpMethod.Get, embedTokens.AccessToken);
            using var embeddedWrite = await SendAsync(http, HttpMethod.Put, embedTokens.AccessToken, Request());
            using var apiKeyRequest = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.AdminWorkCalendar);
            apiKeyRequest.Headers.Add(HeaderNames.ApiKey, app.ApiKey);
            using var apiKeyResponse = await http.SendAsync(apiKeyRequest);
            check("组织管理员的嵌入会话仍不能读写日历，API Key 也不能代替本地管理会话",
                embeddedRead.StatusCode == HttpStatusCode.Forbidden && embeddedWrite.StatusCode == HttpStatusCode.Forbidden &&
                apiKeyResponse.StatusCode == HttpStatusCode.Unauthorized);
            using var anonymous = await http.GetAsync(ApiRoutes.AdminWorkCalendar);
            using var forbidden = await SendAsync(http, HttpMethod.Get, user);
            using var forbiddenWrite = await SendAsync(http, HttpMethod.Put, user, Request());
            using var initial = await SendAsync(http, HttpMethod.Get, admin);
            var original = await initial.Content.ReadFromJsonAsync<WorkCalendarResponse>();
            check("日历匿名拒绝、普通人员读写拒绝，空库回退部署时段及节假日",
                anonymous.StatusCode == HttpStatusCode.Unauthorized && forbidden.StatusCode == HttpStatusCode.Forbidden &&
                forbiddenWrite.StatusCode == HttpStatusCode.Forbidden && original is
                { Revision: 0, TimeZoneId: "Asia/Shanghai", WorkingStart: "08:30", WorkingEnd: "17:30" } &&
                original.Overrides.Single().Date == "2026-10-01");

            var invalidRequests = new[]
            {
                new UpdateWorkCalendarRequest { WorkingStart = "18:00", WorkingEnd = "09:00", Reason = "错误时段" },
                new UpdateWorkCalendarRequest { WorkingStart = "9:00", WorkingEnd = "18:00", Reason = "错误格式" },
                new UpdateWorkCalendarRequest { WorkingStart = "09:00", WorkingEnd = "09:00", Reason = "空的时段" },
                new UpdateWorkCalendarRequest { WorkingStart = "09:00", WorkingEnd = "18:00", Reason = "x" },
                new UpdateWorkCalendarRequest { WorkingStart = "09:00", WorkingEnd = "18:00", Reason = "错误日期",
                    Overrides = [new() { Date = "2026-02-30", IsWorkingDay = false }] },
                new UpdateWorkCalendarRequest { WorkingStart = "09:00", WorkingEnd = "18:00", Reason = "缺少状态",
                    Overrides = [new() { Date = "2026-10-01" }] },
                new UpdateWorkCalendarRequest { WorkingStart = "09:00", WorkingEnd = "18:00", Reason = "重复日期",
                    Overrides = [new() { Date = "2026-10-01", IsWorkingDay = false },
                        new() { Date = "2026-10-01", IsWorkingDay = true }] },
            };
            var rejected = true;
            foreach (var invalid in invalidRequests)
            {
                using var response = await SendAsync(http, HttpMethod.Put, admin, invalid);
                rejected &= response.StatusCode == HttpStatusCode.BadRequest;
            }
            check("日历拒绝跨夜、空时段、非严格时间、无效原因、非法日期、缺失状态和重复日期", rejected);

            var attempts = await Task.WhenAll(SendAsync(http, HttpMethod.Put, admin, Request()),
                SendAsync(http, HttpMethod.Put, admin, Request()));
            check("空库并发保存只有一个成功，另一请求返回 409",
                attempts.Count(item => item.IsSuccessStatusCode) == 1 &&
                attempts.Count(item => item.StatusCode == HttpStatusCode.Conflict) == 1);
            foreach (var response in attempts)
            {
                response.Dispose();
            }
            using var after = await SendAsync(http, HttpMethod.Get, admin);
            var saved = await after.Content.ReadFromJsonAsync<WorkCalendarResponse>();
            check("在线日历持久覆盖部署值，日期排序且部署时区只读",
                saved is { Revision: 1, WorkingStart: "10:00", WorkingEnd: "16:00", TimeZoneId: "Asia/Shanghai" } &&
                saved.Overrides.Select(item => item.Date).SequenceEqual(["2026-10-23", "2026-10-24"]));
            var next = Request();
            next.ExpectedRevision = 1;
            next.Overrides = [];
            using var updated = await SendAsync(http, HttpMethod.Put, admin, next);
            using var stale = await SendAsync(http, HttpMethod.Put, admin, next);
            check("已有日历按预期修订更新，清空覆盖不重新启用部署节假日，旧修订拒绝",
                updated.IsSuccessStatusCode && (await updated.Content.ReadFromJsonAsync<WorkCalendarResponse>()) is
                { Revision: 2, Overrides.Count: 0 } && stale.StatusCode == HttpStatusCode.Conflict);
            await using (var db = await factory.CreateDbContextAsync())
            {
                var audits = await db.OrganizationChanges.Where(item => item.EntityKind == "workCalendar").ToArrayAsync();
                check("有效日历和审计同事务，校验与并发失败不落额外记录",
                    (await db.Set<WorkCalendarEntity>().SingleAsync()).Revision == 2 && audits.Length == 2 &&
                    audits.All(item => item.ActorId == "calendar-admin" && item.AfterJson!.Contains("reason", StringComparison.Ordinal)));
                db.PersonRoles.Remove(await db.PersonRoles.SingleAsync());
                await db.SaveChangesAsync();
            }
            await scope.ServiceProvider.GetRequiredService<INotificationSettingsAppService>().SetDailyDigestTimeAsync("09:00");
            var scheduler = scope.ServiceProvider.GetRequiredService<DailyDigestScheduler>();
            check("在线时段变更后每日汇总仍按部署时区九点触发，不按十点开工延后",
                !await scheduler.ScheduleIfDueAsync(new DateTimeOffset(2026, 10, 24, 0, 59, 0, TimeSpan.Zero)) &&
                await scheduler.ScheduleIfDueAsync(new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero)));
            using var revoked = await SendAsync(http, HttpMethod.Get, admin);
            check("日历请求实时核对组织角色，旧令牌不能保留已撤销管理权限",
                revoked.StatusCode == HttpStatusCode.Forbidden);
        }
        finally { TempDataDirectory.Delete(directory); }
    }

    /// <summary>
    /// 建立可重复提交的完整日历请求。
    /// </summary>
    /// <returns>初始修订零的有效输入。</returns>
    private static UpdateWorkCalendarRequest Request() => new()
    {
        WorkingStart = "10:00",
        WorkingEnd = "16:00",
        Reason = "调整工作安排",
        Overrides = [new() { Date = "2026-10-24", IsWorkingDay = true },
            new() { Date = "2026-10-23", IsWorkingDay = false }],
    };

    /// <summary>
    /// 用人员令牌发送日历请求。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="method">方法。</param>
    /// <param name="token">已签发 JWT。</param>
    /// <param name="body">可选请求体。</param>
    /// <returns>响应，调用方负责释放。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method,
        string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, ApiRoutes.AdminWorkCalendar);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await http.SendAsync(request);
    }
}
