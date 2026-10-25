using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 验证真实 HTTP 管理接口、SQLite 名单持久化及测试回调授权边界。
/// </summary>
internal static class ClientAppAdminChecks
{
    /// <summary>
    /// 执行应用管理员名单场景。
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
                await using var db = await dbFactory.CreateDbContextAsync();
                db.People.AddRange(
                    new PersonEntity { Id = "org-admin", EmployeeNumber = "A100", Name = "组织管理员" },
                    new PersonEntity { Id = "app-admin", EmployeeNumber = "A200", Name = "应用管理员" },
                    new PersonEntity { Id = "ordinary", EmployeeNumber = "A300", Name = "普通人员" },
                    new PersonEntity { Id = "departed", EmployeeNumber = "A400", Name = "离职人员", IsActive = false });
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
                        Code = "active-app",
                        Name = "在用应用",
                        ApiKeyHash = new string('a', 64),
                        SigningSecretCiphertext = "encrypted-secret",
                    },
                    new ClientAppEntity
                    {
                        Code = "other-app",
                        Name = "其他应用",
                        ApiKeyHash = new string('b', 64),
                        SigningSecretCiphertext = "other-secret",
                    });
                await db.SaveChangesAsync();
                var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
                await identity.ProvisionAccountAsync("org-admin", "initial-admin-password");
                await identity.ChangePasswordAsync("org-admin", "initial-admin-password", "normal-admin-password");
                await identity.ProvisionAccountAsync("ordinary", "initial-user-password");
                await identity.ChangePasswordAsync("ordinary", "initial-user-password", "normal-user-password");
            }

            var path = ApiRoutes.AdminAppAdministrators.Replace("{code}", "active-app", StringComparison.Ordinal);
            using (var response = await client.GetAsync(path))
            {
                check("匿名用户不能读取应用管理员名单", response.StatusCode == HttpStatusCode.Unauthorized);
            }

            var ordinaryToken = await LoginAsync(client, "A300", "normal-user-password");
            using (var response = await SendAsync(client, HttpMethod.Get, path, ordinaryToken))
            {
                check("普通人员不能读取应用管理员名单", response.StatusCode == HttpStatusCode.Forbidden);
            }

            var adminToken = await LoginAsync(client, "A100", "normal-admin-password");
            using (var response = await SendAsync(client, HttpMethod.Get, path, adminToken))
            {
                var body = await response.Content.ReadFromJsonAsync<AdminClientAppAdministratorsResponse>();
                check("存量应用默认没有应用管理员", response.IsSuccessStatusCode && body?.PersonIds.Count == 0);
            }

            using (var response = await SendAsync(client, HttpMethod.Put, path, ordinaryToken,
                new UpdateAdminClientAppAdministratorsRequest
                {
                    PersonIds = ["app-admin"],
                    Reason = "接入系统授权",
                }))
            {
                check("普通人员不能写入应用管理员名单", response.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var response = await SendAsync(client, HttpMethod.Put, path, adminToken,
                new UpdateAdminClientAppAdministratorsRequest
                {
                    PersonIds = ["departed"],
                    Reason = "接入系统授权",
                }))
            {
                check("离职人员不能加入应用管理员名单", response.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var response = await SendAsync(client, HttpMethod.Put, path, adminToken,
                new UpdateAdminClientAppAdministratorsRequest
                {
                    PersonIds = ["missing-person"],
                    Reason = "接入系统授权",
                }))
            {
                check("不存在的内部人员不能加入应用管理员名单", response.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var response = await SendAsync(client, HttpMethod.Put, path, adminToken,
                new UpdateAdminClientAppAdministratorsRequest
                {
                    PersonIds = ["app-admin", "app-admin"],
                    Reason = "接入系统授权",
                }))
            {
                check("重复内部人员 ID 被拒绝", response.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var response = await SendAsync(client, HttpMethod.Put, path, adminToken,
                new UpdateAdminClientAppAdministratorsRequest
                {
                    PersonIds = ["app-admin"],
                    Reason = "接入系统授权",
                }))
            {
                var body = await response.Content.ReadFromJsonAsync<AdminClientAppAdministratorsResponse>();
                check("组织管理员可写入应用管理员名单", response.IsSuccessStatusCode &&
                    body?.PersonIds.Single() == "app-admin");
            }

            using (var response = await SendAsync(client, HttpMethod.Put, path, adminToken,
                new UpdateAdminClientAppAdministratorsRequest
                {
                    PersonIds = ["ordinary"],
                    Reason = "并发覆盖尝试",
                }))
            {
                check("旧名单不匹配时拒绝覆盖", response.StatusCode == HttpStatusCode.Conflict);
            }

            using (var scope = server.Services.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ClientAppAdminAppService>();
                check("授权仅限本地会话及指定应用", await service.CanSendTestCallbackAsync("active-app",
                    "app-admin", true) && !await service.CanSendTestCallbackAsync("other-app", "app-admin", true) &&
                    !await service.CanSendTestCallbackAsync("active-app", "app-admin", false));
                check("组织管理员保留已启用应用测试权限", await service.CanSendTestCallbackAsync("other-app",
                    "org-admin", true));
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var audit = await db.OrganizationChanges.AsNoTracking().SingleAsync(item =>
                    item.EntityKind == "clientApp" && item.EntityId == "active-app" &&
                    item.Kind == "administratorsUpdated");
                using var auditJson = JsonDocument.Parse(audit.AfterJson!);
                check("名单变更与人员 ID 和原因审计同事务保存且不含密钥",
                    audit.ActorId == "org-admin" &&
                    auditJson.RootElement.GetProperty("personIds")[0].GetString() == "app-admin" &&
                    auditJson.RootElement.GetProperty("reason").GetString() == "接入系统授权" &&
                    !audit.AfterJson!.Contains("encrypted-secret", StringComparison.Ordinal));
                await db.ClientApps.Where(item => item.Code == "active-app")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, false));
                check("停用应用不能发送测试回调", !await service.CanSendTestCallbackAsync("active-app",
                    "app-admin", true) && !await service.CanSendTestCallbackAsync("active-app", "org-admin", true));
                await db.ClientApps.Where(item => item.Code == "active-app")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, true));
                await db.People.Where(item => item.Id == "app-admin")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false));
                check("离职后名单成员立即失去测试权限", !await service.CanSendTestCallbackAsync("active-app",
                    "app-admin", true));
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 通过真实密码登录获取访问令牌。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="employeeNumber">工号。</param>
    /// <param name="password">密码。</param>
    /// <returns>访问令牌。</returns>
    private static async Task<string> LoginAsync(HttpClient client, string employeeNumber, string password)
    {
        using var response = await client.PostAsJsonAsync(ApiRoutes.IdentityLogin,
            new IdentityLoginRequest(employeeNumber, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdentityTokensResponse>())!.AccessToken;
    }

    /// <summary>
    /// 使用真实 Bearer 身份发送管理请求。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">请求路径。</param>
    /// <param name="token">访问令牌。</param>
    /// <param name="body">可选 JSON 请求体。</param>
    /// <returns>未释放的响应。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }
}
