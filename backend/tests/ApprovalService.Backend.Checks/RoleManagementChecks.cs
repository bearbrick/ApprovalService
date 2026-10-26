using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 真实 JWT、HTTP 与 SQLite 的角色权限和并发场景。
/// </summary>
internal static class RoleManagementChecks
{
    /// <summary>
    /// 验证角色管理完整闭环。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>检查完成。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            await using var server = new ServerFactory(backendRoot, directory, new Dictionary<string, string?>
            {
                ["ApprovalService:Identity:SigningKey"] = Convert.ToBase64String(new byte[64]),
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            });
            using var http = server.CreateClient();
            using var scope = server.Services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                foreach (var id in new[] { "admin", "second", "inactive", "no-account" })
                {
                    db.People.Add(new PersonEntity { Id = id, EmployeeNumber = id, Name = id, IsActive = id != "inactive" });
                }
                db.OrganizationRoles.Add(new OrganizationRoleEntity { Code = "organization-admin", Name = "组织管理员" });
                db.PersonRoles.Add(new PersonRoleEntity { PersonId = "admin", RoleCode = "organization-admin" });
                await db.SaveChangesAsync();
            }
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
            foreach (var id in new[] { "admin", "second" })
            {
                await identity.ProvisionAccountAsync(id, "initial-role-password");
                await identity.ChangePasswordAsync(id, "initial-role-password", "normal-role-password");
            }
            var admin = await LoginAsync(http, "admin");
            var second = await LoginAsync(http, "second");
            string embedded;
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.ClientApps.Add(new ClientAppEntity { Code = "role-app", Name = "角色测试应用", ApiKeyHash = new string('a', 64) });
                db.ExternalPersonBindings.Add(new ExternalPersonBindingEntity
                {
                    ClientAppCode = "role-app",
                    ExternalUserId = "external-admin",
                    PersonId = "admin",
                    Method = "manual",
                });
                await db.SaveChangesAsync();
                var account = await db.LocalAccounts.SingleAsync(item => item.PersonId == "admin");
                embedded = scope.ServiceProvider.GetRequiredService<IIdentityAccessTokenService>().Issue("admin",
                    account.TokenVersion, false, "role-app", "external-admin", 1,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
            }
            using var anonymous = await http.GetAsync(ApiRoutes.AdminRoles);
            using var denied = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminRoles, second);
            using var list = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminRoles, admin);
            var initial = await list.Content.ReadFromJsonAsync<ManagedRoleResponse[]>();
            using var embedDenied = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminRoles, embedded);
            check("角色目录拒绝匿名和普通人员，未落库内置角色显示修订零",
                anonymous.StatusCode == HttpStatusCode.Unauthorized && denied.StatusCode == HttpStatusCode.Forbidden &&
                embedDenied.StatusCode == HttpStatusCode.Forbidden &&
                initial?.Any(role => role.Code == "flow-admin" && role.IsBuiltIn && role.Revision == 0) == true);

            using var invalid = await SendAsync(http, HttpMethod.Post, ApiRoutes.AdminRoles, admin,
                new CreateManagedRoleRequest { Code = "bad/code", Name = "测试", Reason = "创建测试" });
            using var created = await SendAsync(http, HttpMethod.Post, ApiRoutes.AdminRoles, admin,
                new CreateManagedRoleRequest { Code = "finance", Name = "财务", Reason = "创建财务角色" });
            var role = await created.Content.ReadFromJsonAsync<ManagedRoleResponse>();
            using var duplicate = await SendAsync(http, HttpMethod.Post, ApiRoutes.AdminRoles, admin,
                new CreateManagedRoleRequest { Code = "finance", Name = "重复", Reason = "重复创建" });
            using var builtin = await SendAsync(http, HttpMethod.Post, ApiRoutes.AdminRoles, admin,
                new CreateManagedRoleRequest { Code = "flow-admin", Name = "冒用", Reason = "内置创建" });
            check("角色创建验证编码并拒绝重复和内置编码", invalid.StatusCode == HttpStatusCode.BadRequest &&
                role is { Revision: 1, IsBuiltIn: false, Name: "财务" } && duplicate.StatusCode == HttpStatusCode.Conflict &&
                builtin.StatusCode == HttpStatusCode.Conflict);

            var finance = ApiRoutes.AdminRoleByCode.Replace("{code}", "finance", StringComparison.Ordinal);
            var financeMembers = ApiRoutes.AdminRoleMembers.Replace("{code}", "finance", StringComparison.Ordinal);
            using var badReason = await SendAsync(http, HttpMethod.Put, finance, admin,
                new UpdateManagedRoleRequest { Name = "新名称", ExpectedRevision = 1, Reason = "短" });
            using var renamed = await SendAsync(http, HttpMethod.Put, finance, admin,
                new UpdateManagedRoleRequest { Name = "财务审批", ExpectedRevision = 1, Reason = "明确角色用途" });
            using var staleMembers = await SendAsync(http, HttpMethod.Put, financeMembers, admin,
                new UpdateRoleMembersRequest { PersonIds = ["second"], ExpectedRevision = 1, Reason = "旧页成员" });
            using var inactive = await SendAsync(http, HttpMethod.Put, financeMembers, admin,
                new UpdateRoleMembersRequest { PersonIds = ["inactive"], ExpectedRevision = 2, Reason = "新增离职人员" });
            using var missing = await SendAsync(http, HttpMethod.Put, financeMembers, admin,
                new UpdateRoleMembersRequest { PersonIds = ["missing"], ExpectedRevision = 2, Reason = "新增不存在人员" });
            using var repeated = await SendAsync(http, HttpMethod.Put, financeMembers, admin,
                new UpdateRoleMembersRequest { PersonIds = ["second", "second"], ExpectedRevision = 2, Reason = "重复人员" });
            using var members = await SendAsync(http, HttpMethod.Put, financeMembers, admin,
                new UpdateRoleMembersRequest { PersonIds = ["second"], ExpectedRevision = 2, Reason = "添加在职人员" });
            var changed = await members.Content.ReadFromJsonAsync<ManagedRoleResponse>();
            check("名称和成员共用修订，原因、在职、存在和去重规则有效", badReason.StatusCode == HttpStatusCode.BadRequest &&
                renamed.IsSuccessStatusCode && staleMembers.StatusCode == HttpStatusCode.Conflict &&
                inactive.StatusCode == HttpStatusCode.BadRequest && missing.StatusCode == HttpStatusCode.BadRequest &&
                repeated.StatusCode == HttpStatusCode.BadRequest && changed is { Revision: 3, Name: "财务审批" } &&
                changed.PersonIds.SequenceEqual(["second"]));

            using var detail = await SendAsync(http, HttpMethod.Get, finance, admin);
            using var memberDetail = await SendAsync(http, HttpMethod.Get, financeMembers, admin);
            using var deletion = await SendAsync(http, HttpMethod.Delete, finance, admin);
            check("角色详情与成员读取一致且没有删除入口", detail.IsSuccessStatusCode &&
                await detail.Content.ReadAsStringAsync() == await memberDetail.Content.ReadAsStringAsync() &&
                deletion.StatusCode == HttpStatusCode.MethodNotAllowed);

            var admins = ApiRoutes.AdminRoleMembers.Replace("{code}", "organization-admin", StringComparison.Ordinal);
            using var last = await SendAsync(http, HttpMethod.Put, admins, admin,
                new UpdateRoleMembersRequest { PersonIds = [], ExpectedRevision = 0, Reason = "删除最后管理员" });
            using var caseLast = await SendAsync(http, HttpMethod.Put, ApiRoutes.AdminRoleMembers.Replace("{code}", "ORGANIZATION-ADMIN", StringComparison.Ordinal), admin,
                new UpdateRoleMembersRequest { PersonIds = [], ExpectedRevision = 0, Reason = "大小写路径保护" });
            using var noAccount = await SendAsync(http, HttpMethod.Put, admins, admin,
                new UpdateRoleMembersRequest { PersonIds = ["no-account"], ExpectedRevision = 0, Reason = "替换未开通人员" });
            using var addAdmin = await SendAsync(http, HttpMethod.Put, admins, admin,
                new UpdateRoleMembersRequest { PersonIds = ["admin", "second"], ExpectedRevision = 0, Reason = "增加第二管理员" });
            using var newlyAllowed = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminRoles, second);
            check("最后一位可登录管理员受到保护，新增管理员旧 JWT 立即获权",
                last.StatusCode == HttpStatusCode.Conflict && caseLast.StatusCode == HttpStatusCode.Conflict && noAccount.StatusCode == HttpStatusCode.Conflict &&
                addAdmin.IsSuccessStatusCode && newlyAllowed.IsSuccessStatusCode);
            using var revoke = await SendAsync(http, HttpMethod.Put, admins, second,
                new UpdateRoleMembersRequest { PersonIds = ["second"], ExpectedRevision = 1, Reason = "移交管理职责" });
            using var oldToken = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminRoles, admin);
            using var staleAdmin = await SendAsync(http, HttpMethod.Put, admins, second,
                new UpdateRoleMembersRequest { PersonIds = ["admin"], ExpectedRevision = 1, Reason = "过期页面覆盖" });
            check("撤销管理员后旧 JWT 立即失权，过期成员页面不能恢复旧权限",
                revoke.IsSuccessStatusCode && oldToken.StatusCode == HttpStatusCode.Forbidden &&
                staleAdmin.StatusCode == HttpStatusCode.Conflict);

            await using var verify = await factory.CreateDbContextAsync();
            var audits = await verify.OrganizationChanges.Where(item => item.EntityKind == "role").ToListAsync();
            check("仅成功角色变更留下原子原因审计与真实操作者", audits.Count == 5 &&
                audits.All(item => item.AfterJson!.Contains("reason", StringComparison.Ordinal)) &&
                audits.Any(item => item.ActorId == "second" && item.EntityId == "organization-admin") &&
                await verify.PersonRoles.CountAsync(item => item.RoleCode == "organization-admin") == 1);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 登录已开通的测试人员。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="id">工号。</param>
    /// <returns>访问令牌。</returns>
    private static async Task<string> LoginAsync(HttpClient http, string id)
    {
        using var response = await http.PostAsJsonAsync(ApiRoutes.IdentityLogin, new IdentityLoginRequest(id, "normal-role-password"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdentityTokensResponse>())!.AccessToken;
    }

    /// <summary>
    /// 发出带本地人员 JWT 的请求。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="method">请求方法。</param>
    /// <param name="path">契约路径。</param>
    /// <param name="token">访问令牌。</param>
    /// <param name="body">可选正文。</param>
    /// <returns>调用者释放的响应。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path,
        string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await http.SendAsync(request);
    }
}
