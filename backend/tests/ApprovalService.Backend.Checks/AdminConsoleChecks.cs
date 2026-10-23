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
/// 用真实管理路由验证登录权限、组织摘要和密钥隔离。
/// </summary>
internal static class AdminConsoleChecks
{
    /// <summary>
    /// 执行管理后台的读取场景。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <param name="check">检查结果记录器。</param>
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
            await using var factory = new ServerFactory(backendRoot, directory, settings);
            using var client = factory.CreateClient();
            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using (var db = await dbFactory.CreateDbContextAsync())
                {
                    db.People.AddRange(
                        new PersonEntity { Id = "admin", EmployeeNumber = "A001", Name = "管理员" },
                        new PersonEntity
                        {
                            Id = "viewer",
                            EmployeeNumber = "V001",
                            Name = "普通人员",
                            Position = "clerk",
                            Phone = "13900000000"
                        });
                    db.Departments.Add(new DepartmentEntity
                    {
                        Id = "department",
                        Code = "D001",
                        Name = "业务部",
                        HeadPersonId = "admin",
                    });
                    db.PersonDepartments.Add(new PersonDepartmentEntity
                    {
                        PersonId = "viewer",
                        DepartmentId = "department",
                        IsPrimary = true,
                    });
                    db.OrganizationRoles.Add(new OrganizationRoleEntity
                    {
                        Code = "organization-admin",
                        Name = "组织管理员",
                    });
                    db.PersonRoles.Add(new PersonRoleEntity
                    {
                        PersonId = "admin",
                        RoleCode = "organization-admin",
                    });
                    db.ClientApps.Add(new ClientAppEntity
                    {
                        Code = "sample",
                        Name = "示例系统",
                        ApiKeyHash = new string('a', 64),
                        SigningSecretCiphertext = "sensitive-ciphertext",
                        AllowedFlowCodesJson = "[\"EXPENSE\"]",
                    });
                    await db.SaveChangesAsync();
                }

                var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
                await identity.ProvisionAccountAsync("admin", "initial-admin-password");
                await identity.ChangePasswordAsync("admin", "initial-admin-password", "normal-admin-password");
                await identity.ProvisionAccountAsync("viewer", "initial-viewer-password");
                await identity.ChangePasswordAsync("viewer", "initial-viewer-password", "normal-viewer-password");
            }

            using (var anonymous = await client.GetAsync(ApiRoutes.AdminDirectory))
            {
                check("匿名用户不能读取管理组织目录", anonymous.StatusCode == HttpStatusCode.Unauthorized);
            }

            var viewer = await LoginAsync(client, "V001", "normal-viewer-password");
            using (var forbidden = await GetAuthorizedAsync(client, ApiRoutes.AdminClientApps, viewer.AccessToken))
            {
                check("普通登录人员不能读取接入应用列表", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            var admin = await LoginAsync(client, "A001", "normal-admin-password");
            using (var session = await GetAuthorizedAsync(client, ApiRoutes.AdminSession, admin.AccessToken))
            {
                var body = await session.Content.ReadFromJsonAsync<AdminSessionResponse>();
                check("本地组织管理员可核验管理会话", session.IsSuccessStatusCode && body?.PersonId == "admin");
            }

            using (var response = await GetAuthorizedAsync(client, ApiRoutes.AdminDirectory, admin.AccessToken))
            {
                var body = await response.Content.ReadFromJsonAsync<AdminDirectoryResponse>();
                var raw = await response.Content.ReadAsStringAsync();
                check("管理目录从真实数据库读取部门、人员及负责人关系",
                    response.IsSuccessStatusCode && body?.Departments.Single().ManagerName == "管理员" &&
                    body.People.Single(item => item.Id == "viewer").Departments.Single() == "业务部");
                check("组织管理列表不输出人员联系方式", !raw.Contains("13900000000", StringComparison.Ordinal));
            }

            using (var response = await GetAuthorizedAsync(client, ApiRoutes.AdminClientApps, admin.AccessToken))
            {
                var body = await response.Content.ReadFromJsonAsync<AdminClientAppResponse[]>();
                var raw = await response.Content.ReadAsStringAsync();
                check("接入应用列表只输出名称、编码、授权和启停状态",
                    response.IsSuccessStatusCode && body?.Single().Code == "sample" &&
                    body.Single().AllowedFlowCodes.Single() == "EXPENSE");
                check("接入应用列表不泄露 API Key 摘要和签名密钥密文",
                    !raw.Contains(new string('a', 64), StringComparison.Ordinal) &&
                    !raw.Contains("sensitive-ciphertext", StringComparison.Ordinal));
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 通过真实登录接口取得人员会话。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="employeeNumber">工号。</param>
    /// <param name="password">密码。</param>
    /// <returns>身份令牌。</returns>
    private static async Task<IdentityTokensResponse> LoginAsync(HttpClient client, string employeeNumber, string password)
    {
        using var response = await client.PostAsJsonAsync(ApiRoutes.IdentityLogin,
            new IdentityLoginRequest(employeeNumber, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdentityTokensResponse>())!;
    }

    /// <summary>
    /// 带已签发的访问令牌读取管理资源。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="path">管理资源地址。</param>
    /// <param name="token">访问令牌。</param>
    /// <returns>未释放的 HTTP 响应。</returns>
    private static async Task<HttpResponseMessage> GetAuthorizedAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
