using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 通过进程内 HTTP 请求检查账号、接入应用及管理授权隔离。
/// </summary>
internal static class IdentityEndpointChecks
{
    /// <summary>
    /// 执行认证边界场景。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
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
                    db.People.Add(new PersonEntity
                    {
                        Id = "admin-person",
                        EmployeeNumber = "ADMIN-001",
                        Name = "组织管理员",
                    });
                    db.OrganizationRoles.Add(new OrganizationRoleEntity
                    {
                        Code = "organization-admin",
                        Name = "组织管理员",
                    });
                    db.PersonRoles.Add(new PersonRoleEntity
                    {
                        PersonId = "admin-person",
                        RoleCode = "organization-admin",
                    });
                    await db.SaveChangesAsync();
                }

                await scope.ServiceProvider.GetRequiredService<IIdentityAppService>()
                    .ProvisionAccountAsync("admin-person", "temporary-password-123");
            }

            var secrets = await CreateClientAppAsync(factory.Services, "demo");
            var otherSecrets = await CreateClientAppAsync(factory.Services, "other");
            var first = await LoginAsync(client, "temporary-password-123");
            check("首次登录令牌标记必须改密", first.MustChangePassword);
            using (var template = await GetAuthorizedAsync(client, ApiRoutes.OrgTemplate, first.AccessToken))
            {
                check("首次登录未改密前不能访问组织管理接口", template.StatusCode == HttpStatusCode.Unauthorized);
            }

            using (var change = await PostAuthorizedAsync(client, ApiRoutes.IdentityChangePassword,
                new IdentityChangePasswordRequest("temporary-password-123", "new-password-123"),
                first.AccessToken))
            {
                check("首次登录令牌只允许修改自己的密码", change.StatusCode == HttpStatusCode.NoContent);
            }

            var local = await LoginAsync(client, "new-password-123");
            using (var template = await GetAuthorizedAsync(client, ApiRoutes.OrgTemplate, local.AccessToken))
            {
                check("本地密码管理员令牌可下载组织模板", template.StatusCode == HttpStatusCode.OK);
            }

            using (var unsigned = await client.PostAsJsonAsync(ApiRoutes.EmbedTokens,
                new EmbedTokenRequest("external-admin", "#/m/tasks")))
            {
                check("嵌入签发缺少 API Key 返回未授权", unsigned.StatusCode == HttpStatusCode.Unauthorized);
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.OrgChanges))
            {
                request.Headers.Add(HeaderNames.ApiKey, secrets.ApiKey);
                request.Content = JsonContent.Create(new
                {
                    departments = Array.Empty<object>(),
                    people = new object?[] { null },
                    memberships = Array.Empty<object>(),
                });
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
                check("组织变更列表中的 null 项返回 400 而不是内部错误",
                    response.StatusCode == HttpStatusCode.BadRequest && body?.Code == ErrorCodes.ValidationFailed);
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.EmbedTokens))
            {
                request.Headers.Add(HeaderNames.ApiKey, secrets.ApiKey);
                request.Content = JsonContent.Create(new EmbedTokenRequest("ADMIN-001", "#/m/tasks"));
                using var response = await client.SendAsync(request);
                check("未绑定人员不能借全局工号签发嵌入令牌", response.StatusCode == HttpStatusCode.NotFound);
            }

            using (var binding = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.OrgBindings))
            {
                binding.Headers.Add(HeaderNames.ApiKey, secrets.ApiKey);
                binding.Content = JsonContent.Create(new OrganizationBindingRequest("person", "external-admin",
                    "admin-person", null, null, "push", null));
                using var response = await client.SendAsync(binding);
                check("API Key 不能指定任意内部人员 ID 建立绑定", response.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var binding = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.OrgBindings))
            {
                binding.Headers.Add(HeaderNames.ApiKey, secrets.ApiKey);
                binding.Content = JsonContent.Create(new OrganizationBindingRequest("person", "external-admin",
                    null, "ADMIN-001", null, "push", null));
                using var response = await client.SendAsync(binding);
                check("API Key 不能按工号绑定审批服务可信来源的管理员", response.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var binding = await PostAuthorizedAsync(client, ApiRoutes.AdminOrgBindings,
                new OrganizationBindingRequest("person", "external-admin", "admin-person",
                    null, null, "manual", "demo"), local.AccessToken))
            {
                check("本地组织管理员可确认跨来源人员绑定", binding.StatusCode == HttpStatusCode.OK);
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.EmbedTokens))
            {
                request.Headers.Add(HeaderNames.ApiKey, otherSecrets.ApiKey);
                request.Content = JsonContent.Create(new EmbedTokenRequest("external-admin", "#/m/tasks"));
                using var response = await client.SendAsync(request);
                check("另一个接入应用不能使用本应用的绑定签发嵌入令牌",
                    response.StatusCode == HttpStatusCode.NotFound);
            }

            string embedToken;
            using (var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.EmbedTokens))
            {
                request.Headers.Add(HeaderNames.ApiKey, secrets.ApiKey);
                request.Content = JsonContent.Create(new EmbedTokenRequest("external-admin", "#/m/tasks"));
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                var token = JsonSerializer.Deserialize<EmbedTokenResponse>(body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                check("接入应用只能为可信绑定人员签发嵌入令牌",
                    response.IsSuccessStatusCode && token?.Token.Length > 20);
                embedToken = token!.Token;
            }

            IdentityTokensResponse embedded;
            using (var exchange = await client.PostAsJsonAsync(ApiRoutes.EmbedTokenExchange,
                new EmbedTokenExchangeRequest(embedToken)))
            {
                embedded = (await exchange.Content.ReadFromJsonAsync<IdentityTokensResponse>())!;
                check("一次性嵌入交换得到绑定人员和站内路径",
                    exchange.IsSuccessStatusCode && embedded.PersonId == "admin-person" &&
                    embedded.TargetPath == "#/m/tasks");
            }

            using (var template = await GetAuthorizedAsync(client, ApiRoutes.OrgTemplate, embedded.AccessToken))
            {
                check("嵌入会话即使绑定管理员也不能访问管理接口", template.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var refresh = await client.PostAsJsonAsync(ApiRoutes.IdentityRefresh,
                new IdentityRefreshRequest(embedded.RefreshToken)))
            {
                var refreshed = (await refresh.Content.ReadFromJsonAsync<IdentityTokensResponse>())!;
                using var template = await GetAuthorizedAsync(client, ApiRoutes.OrgTemplate, refreshed.AccessToken);
                check("刷新嵌入会话不会洗成管理员权限", refresh.IsSuccessStatusCode &&
                    template.StatusCode == HttpStatusCode.Forbidden);
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 在检查专用库中创建已启用的接入应用。
    /// </summary>
    /// <param name="services">服务容器。</param>
    /// <param name="code">应用编码。</param>
    /// <returns>一次性返回的接入应用密钥。</returns>
    private static async Task<ClientAppSecretsDto> CreateClientAppAsync(IServiceProvider services, string code)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ClientAppAppService>()
            .CreateAsync(new CreateClientAppInput
            {
                Code = code,
                Name = "测试业务系统",
                AllowedFlowCodes = [],
            });
    }

    /// <summary>
    /// 调用真实登录路由并读取令牌。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="password">本次密码。</param>
    /// <returns>登录令牌。</returns>
    private static async Task<IdentityTokensResponse> LoginAsync(HttpClient client, string password)
    {
        using var response = await client.PostAsJsonAsync(ApiRoutes.IdentityLogin,
            new IdentityLoginRequest("ADMIN-001", password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdentityTokensResponse>())!;
    }

    /// <summary>
    /// 携带访问令牌读取一个管理端资源。
    /// </summary>
    /// <param name="client">HTTP 客户端。</param>
    /// <param name="path">资源路径。</param>
    /// <param name="token">JWT。</param>
    /// <returns>未释放的响应。</returns>
    private static async Task<HttpResponseMessage> GetAuthorizedAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// 携带访问令牌发送一个 JSON 请求。
    /// </summary>
    /// <param name="client">HTTP 客户端。</param>
    /// <param name="path">目标路径。</param>
    /// <param name="body">JSON 请求体。</param>
    /// <param name="token">JWT。</param>
    /// <returns>未释放的响应。</returns>
    private static async Task<HttpResponseMessage> PostAuthorizedAsync(HttpClient client, string path,
        object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
