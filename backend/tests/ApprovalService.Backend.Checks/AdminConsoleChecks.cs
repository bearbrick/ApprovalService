using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
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
                    db.Flows.Add(new FlowEntity
                    {
                        Id = "flow",
                        Code = "EXPENSE",
                        Name = "费用",
                        PublishedVersionId = "version",
                    });
                    db.FlowVersions.Add(new FlowVersionEntity
                    {
                        Id = "version",
                        FlowId = "flow",
                        Version = 1,
                        SchemaVersion = 1,
                        DefinitionJson = "{}",
                        GraphJson = "{}",
                    });
                    db.Instances.Add(new InstanceEntity
                    {
                        Id = "instance",
                        SerialNo = "EXP-1",
                        FlowId = "flow",
                        FlowVersionId = "version",
                        FlowCode = "EXPENSE",
                        BusinessKey = "B-1",
                        Title = "费用申请",
                        InitiatorId = "admin",
                        ClientAppCode = "sample",
                        Status = "approved",
                        StateJson = "{}",
                    });
                    db.OutboxItems.Add(new OutboxItemEntity
                    {
                        Id = "outbox",
                        InstanceId = "instance",
                        EventId = "callback-event",
                        Kind = "callback",
                        PayloadJson = "{\"private\":\"business-payload\"}",
                        Status = "completed",
                        Attempts = 3,
                        LastError = "TimeoutException",
                        AvailableAt = DateTimeOffset.UtcNow.AddHours(-1),
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

            using (var forbidden = await GetAuthorizedAsync(client, ApiRoutes.AdminOutbox, viewer.AccessToken))
            {
                check("普通人员不能读取待发队列", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var response = await GetAuthorizedAsync(client, ApiRoutes.AdminOutbox, admin.AccessToken))
            {
                var queue = await response.Content.ReadFromJsonAsync<AdminOutboxResponse[]>();
                var raw = await response.Content.ReadAsStringAsync();
                check("管理员可读取所属应用、事件状态和错误类别", response.IsSuccessStatusCode &&
                    queue?.Single().EventId == "callback-event" &&
                    queue.Single().ClientAppCode == "sample" && queue.Single().Attempts == 3 &&
                    queue.Single().ErrorCode == "TimeoutException");
                check("队列摘要不泄露业务正文", !raw.Contains("business-payload", StringComparison.Ordinal));
            }

            var retryPath = ApiRoutes.AdminCallbackRetry.Replace("{eventId}", "callback-event",
                StringComparison.Ordinal);
            using (var invalid = await PostAuthorizedAsync(client, retryPath,
                new AdminCallbackRetryRequest { Reason = "" }, admin.AccessToken))
            {
                check("回调手动重发必须填写原因", invalid.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var retried = await PostAuthorizedAsync(client, retryPath,
                new AdminCallbackRetryRequest { Reason = "试点故障后重新投递" }, admin.AccessToken))
            {
                check("管理员可用原事件 ID 重排回调", retried.StatusCode == HttpStatusCode.NoContent);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var retryAudit = await db.OrganizationChanges.AsNoTracking()
                    .SingleAsync(item => item.EntityKind == "callback" && item.EntityId == "callback-event");
                check("回调重发以原事件 ID 记录管理员和原因且不含正文",
                    retryAudit.ActorId == "admin" && retryAudit.Kind == "retried" &&
                    retryAudit.AfterJson is { } json && json.Contains("reason", StringComparison.Ordinal) &&
                    !json.Contains("business-payload", StringComparison.Ordinal));
            }

            var originsPath = ApiRoutes.AdminAppEmbedOrigins.Replace("{code}", "sample", StringComparison.Ordinal);
            using (var forbidden = await GetAuthorizedAsync(client, originsPath, viewer.AccessToken))
            {
                check("普通人员不能读取应用嵌入来源设置", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var invalid = await PutAuthorizedAsync(client, originsPath,
                new UpdateAdminEmbedOriginsRequest
                {
                    Origins = ["https://erp.example.com/path"],
                    Reason = "试点接入",
                }, admin.AccessToken))
            {
                check("嵌入来源拒绝带路径的网站地址", invalid.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var invalid = await PutAuthorizedAsync(client, originsPath,
                new UpdateAdminEmbedOriginsRequest
                {
                    Origins = ["http://erp.example.com"],
                    Reason = "试点接入",
                }, admin.AccessToken))
            {
                check("嵌入来源拒绝非本机 HTTP 网站", invalid.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var invalid = await PutAuthorizedAsync(client, originsPath,
                new UpdateAdminEmbedOriginsRequest
                {
                    Origins = ["https://erp.example.com"],
                    Reason = "",
                }, admin.AccessToken))
            {
                check("修改嵌入来源必须填写原因", invalid.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var saved = await PutAuthorizedAsync(client, originsPath,
                new UpdateAdminEmbedOriginsRequest
                {
                    Origins = ["https://erp.example.com", "https://erp.example.com", "http://localhost:5273"],
                    Reason = "试点系统嵌入审批中心",
                }, admin.AccessToken))
            {
                var body = await saved.Content.ReadFromJsonAsync<AdminEmbedOriginsResponse>();
                check("管理员保存合法嵌入来源时去重并允许本机开发地址",
                    saved.IsSuccessStatusCode && body?.Origins.Count == 2 &&
                    body.Origins.Contains("https://erp.example.com") && body.Origins.Contains("http://localhost:5273"));
            }

            using (var response = await GetAuthorizedAsync(client, originsPath, admin.AccessToken))
            {
                var body = await response.Content.ReadFromJsonAsync<AdminEmbedOriginsResponse>();
                check("重新读取应用嵌入来源返回持久设置",
                    response.IsSuccessStatusCode && body?.Code == "sample" && body.Origins.Count == 2);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var audit = await db.OrganizationChanges.AsNoTracking()
                    .SingleAsync(item => item.EntityKind == "clientApp" && item.EntityId == "sample");
                var auditJson = audit.AfterJson ?? "{}";
                using var auditSnapshot = JsonDocument.Parse(auditJson);
                check("来源变更与管理员、原因和不含密钥的快照一起持久留痕",
                    audit.ActorId == "admin" &&
                    auditSnapshot.RootElement.GetProperty("reason").GetString() == "试点系统嵌入审批中心" &&
                    !auditJson.Contains("sensitive-ciphertext", StringComparison.Ordinal) &&
                    !auditJson.Contains(new string('a', 64), StringComparison.Ordinal));
            }

            using (var forbidden = await GetAuthorizedAsync(client, ApiRoutes.AdminBindings, viewer.AccessToken))
            {
                check("普通人员不能读取业务系统绑定", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var bound = await PostAuthorizedAsync(client, ApiRoutes.AdminOrgBindings,
                new OrganizationBindingRequest("person", "erp-user-1", "viewer", null, null,
                    "manual", "sample"), admin.AccessToken))
            {
                check("管理员可经现有绑定接口建立人员映射", bound.IsSuccessStatusCode);
            }

            using (var response = await GetAuthorizedAsync(client, ApiRoutes.AdminBindings, admin.AccessToken))
            {
                var bindings = await response.Content.ReadFromJsonAsync<AdminBindingResponse[]>();
                var raw = await response.Content.ReadAsStringAsync();
                check("管理员可读取带当前内部名称的业务系统绑定", response.IsSuccessStatusCode &&
                    bindings?.Single().ClientAppCode == "sample" &&
                    bindings.Single().ExternalId == "erp-user-1" &&
                    bindings.Single().TargetName == "普通人员");
                check("绑定列表不泄露人员联系方式和应用密钥",
                    !raw.Contains("13900000000", StringComparison.Ordinal) &&
                    !raw.Contains("sensitive-ciphertext", StringComparison.Ordinal));
            }

            // 人员重新绑定会推进令牌代际；再次登录后仍须由当前角色拒绝审计读取。
            var viewerAfterBinding = await LoginAsync(client, "V001", "normal-viewer-password");
            using (var forbidden = await GetAuthorizedAsync(client, ApiRoutes.AdminAudit,
                viewerAfterBinding.AccessToken))
            {
                check("普通人员不能读取管理员操作日志", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var response = await GetAuthorizedAsync(client, ApiRoutes.AdminAudit, admin.AccessToken))
            {
                var audit = await response.Content.ReadFromJsonAsync<AdminAuditResponse[]>();
                var raw = await response.Content.ReadAsStringAsync();
                check("管理员操作日志只列出最近的组织及应用变更元数据和显式原因",
                    response.IsSuccessStatusCode && audit?.Any(item => item.Action == "embedOriginsUpdated" &&
                        item.ActorId == "admin" && item.Reason == "试点系统嵌入审批中心") == true &&
                    audit.Any(item => item.Target == "personBinding:sample:erp-user-1"));
                check("操作日志不返回人员联系方式、完整快照与应用密钥",
                    !raw.Contains("13900000000", StringComparison.Ordinal) &&
                    !raw.Contains("sensitive-ciphertext", StringComparison.Ordinal) &&
                    !raw.Contains("beforeJson", StringComparison.OrdinalIgnoreCase) &&
                    !raw.Contains("afterJson", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// 带管理员访问令牌提交 JSON 设置。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="path">管理设置地址。</param>
    /// <param name="body">待保存请求。</param>
    /// <param name="token">访问令牌。</param>
    /// <returns>未释放的 HTTP 响应。</returns>
    private static async Task<HttpResponseMessage> PutAuthorizedAsync(HttpClient client, string path,
        UpdateAdminEmbedOriginsRequest body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// 使用管理员访问令牌建立业务系统绑定。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="path">绑定接口地址。</param>
    /// <param name="body">绑定请求。</param>
    /// <param name="token">访问令牌。</param>
    /// <returns>未释放的 HTTP 响应。</returns>
    private static async Task<HttpResponseMessage> PostAuthorizedAsync(HttpClient client, string path,
        object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
