using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 JWT、HTTP 和 SQLite 验证流程草稿、部门范围与不可变发布。
/// </summary>
internal static class FlowManagementHttpChecks
{
    /// <summary>
    /// 执行流程管理后端场景。
    /// </summary>
    /// <param name="backendRoot">后端项目根目录。</param>
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
            await SeedAsync(factory);
            using (var anonymous = await client.GetAsync(ApiRoutes.AdminFlows))
            {
                check("匿名访问流程管理列表返回未授权", anonymous.StatusCode == HttpStatusCode.Unauthorized);
            }

            var admin = await LoginAsync(client, "A001", "admin-password-new");
            var manager = await LoginAsync(client, "M001", "manager-password-new");
            var parentManager = await LoginAsync(client, "P001", "parent-password-new");
            var unassignedManager = await LoginAsync(client, "U001", "unassigned-password-new");
            var viewer = await LoginAsync(client, "V001", "viewer-password-new");
            using (var anonymousDepartments = await client.GetAsync(ApiRoutes.AdminFlowDepartments))
            {
                check("匿名不能读取流程所属部门选项",
                    anonymousDepartments.StatusCode == HttpStatusCode.Unauthorized);
            }

            using (var adminDepartments = await SendAsync(client, HttpMethod.Get,
                ApiRoutes.AdminFlowDepartments, admin))
            {
                var body = await adminDepartments.Content.ReadFromJsonAsync<AdminFlowDepartmentOption[]>();
                check("组织管理员只获得所有现存部门的内部 ID 和名称", adminDepartments.IsSuccessStatusCode &&
                    body is { Length: 2 } && body.Select(item => item.Id).OrderBy(item => item)
                        .SequenceEqual(["child", "parent"]));
            }

            using (var ownDepartments = await SendAsync(client, HttpMethod.Get,
                ApiRoutes.AdminFlowDepartments, manager))
            {
                var body = await ownDepartments.Content.ReadFromJsonAsync<AdminFlowDepartmentOption[]>();
                check("部门流程管理员只看直接加入的现存部门", ownDepartments.IsSuccessStatusCode &&
                    body is [{ Id: "child", Name: "直属部门" }]);
            }

            using (var parentDepartments = await SendAsync(client, HttpMethod.Get,
                ApiRoutes.AdminFlowDepartments, parentManager))
            {
                var body = await parentDepartments.Content.ReadFromJsonAsync<AdminFlowDepartmentOption[]>();
                check("上级部门流程管理员不继承下级部门选项", parentDepartments.IsSuccessStatusCode &&
                    body is [{ Id: "parent", Name: "上级部门" }]);
            }

            using (var unassigned = await SendAsync(client, HttpMethod.Get,
                ApiRoutes.AdminFlowDepartments, unassignedManager))
            {
                var body = await unassigned.Content.ReadFromJsonAsync<AdminFlowDepartmentOption[]>();
                check("没有直属部门的流程管理员仅得到空选项", unassigned.IsSuccessStatusCode &&
                    body is { Length: 0 });
            }

            using (var forbiddenDepartments = await SendAsync(client, HttpMethod.Get,
                ApiRoutes.AdminFlowDepartments, viewer))
            {
                check("普通本地人员不能读取流程部门选项",
                    forbiddenDepartments.StatusCode == HttpStatusCode.Forbidden);
            }
            using (var forbidden = await SendAsync(client, HttpMethod.Get, ApiRoutes.AdminFlows, viewer))
            {
                check("普通本地人员不能读取流程管理列表", forbidden.StatusCode == HttpStatusCode.Forbidden);
            }

            var definition = ReadSample(backendRoot);
            definition.Code = "FLOW_MANAGED";
            definition.Name = "受管理流程";
            using var created = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlows, admin,
                new AdminFlowCreateRequest
                {
                    Definition = definition,
                    OwnerDepartmentId = "child",
                    AdministratorId = "manager",
                    Reason = "建立部门审批流程",
                });
            var first = await created.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("组织管理员可创建归属部门流程及修订一的草稿",
                created.IsSuccessStatusCode && first is { Code: "FLOW_MANAGED", DraftRevision: 1 } &&
                first.OwnerDepartmentId == "child" && first.PublishedVersionId is null);

            var flowPath = ApiRoutes.AdminFlowByCode.Replace("{code}", "FLOW_MANAGED", StringComparison.Ordinal);
            using (var own = await SendAsync(client, HttpMethod.Get, flowPath, manager))
            {
                var body = await own.Content.ReadFromJsonAsync<AdminFlowResponse>();
                check("本地部门流程管理员能读取直接所属部门草稿",
                    own.IsSuccessStatusCode && body?.Draft?.Code == "FLOW_MANAGED");
            }

            using (var outside = await SendAsync(client, HttpMethod.Get, flowPath, parentManager))
            {
                check("上级部门管理员不能穿透读取下级流程", outside.StatusCode == HttpStatusCode.NotFound);
            }

            using (var listed = await SendAsync(client, HttpMethod.Get, ApiRoutes.AdminFlows, parentManager))
            {
                var body = await listed.Content.ReadFromJsonAsync<AdminFlowResponse[]>();
                check("流程目录只投影本人直接部门可管理的流程", listed.IsSuccessStatusCode && body?.Length == 0);
            }

            var draftPath = ApiRoutes.AdminFlowDraft.Replace("{code}", "FLOW_MANAGED", StringComparison.Ordinal);
            using (var stale = await SendAsync(client, HttpMethod.Put, draftPath, manager,
                new AdminFlowDraftRequest
                {
                    ExpectedRevision = 0,
                    Definition = definition,
                    OwnerDepartmentId = "child",
                    Reason = "使用旧修订保存",
                }))
            {
                check("草稿旧修订返回冲突", stale.StatusCode == HttpStatusCode.Conflict);
            }

            using (var moved = await SendAsync(client, HttpMethod.Put, draftPath, manager,
                new AdminFlowDraftRequest
                {
                    ExpectedRevision = 1,
                    Definition = definition,
                    OwnerDepartmentId = "parent",
                    Reason = "尝试迁往非直属部门",
                }))
            {
                check("部门流程管理员不能把流程移到非直属部门", moved.StatusCode == HttpStatusCode.Forbidden);
            }

            var publishPath = ApiRoutes.AdminFlowPublish.Replace("{code}", "FLOW_MANAGED", StringComparison.Ordinal);
            var firstPublish = new AdminFlowPublishRequest
            {
                ExpectedRevision = 1,
                Reason = "完成首次发布检查",
            };
            using var published = await SendAsync(client, HttpMethod.Post, publishPath, manager, firstPublish);
            var publishedBody = await published.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("发布编译执行图并追加不可变第一版", published.IsSuccessStatusCode &&
                publishedBody is { PublishedVersion: 1 } && publishedBody.PublishedVersionId is not null);

            using (var repeated = await SendAsync(client, HttpMethod.Post, publishPath, manager, firstPublish))
            {
                check("旧当前发布版前提不能覆盖新发布版", repeated.StatusCode == HttpStatusCode.Conflict);
            }

            definition.Name = "受管理流程第二稿";
            using var saved = await SendAsync(client, HttpMethod.Put, draftPath, manager,
                new AdminFlowDraftRequest
                {
                    ExpectedRevision = 1,
                    Definition = definition,
                    OwnerDepartmentId = "child",
                    AdministratorId = "manager",
                    Reason = "调整第二稿名称",
                });
            var savedBody = await saved.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("按当前修订保存后只递增草稿修订", saved.IsSuccessStatusCode &&
                savedBody is { DraftRevision: 2, PublishedVersion: 1 });

            using (var staleDraft = await SendAsync(client, HttpMethod.Post, publishPath, manager,
                new AdminFlowPublishRequest
                {
                    ExpectedRevision = 1,
                    ExpectedPublishedVersionId = publishedBody?.PublishedVersionId,
                    Reason = "尝试发布旧草稿",
                }))
            {
                check("发布必须同时核对最新草稿修订", staleDraft.StatusCode == HttpStatusCode.Conflict);
            }

            using var second = await SendAsync(client, HttpMethod.Post, publishPath, manager,
                new AdminFlowPublishRequest
                {
                    ExpectedRevision = 2,
                    ExpectedPublishedVersionId = publishedBody?.PublishedVersionId,
                    Reason = "发布第二版",
                });
            var secondBody = await second.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("新发布只追加第二版并切换当前指针", second.IsSuccessStatusCode &&
                secondBody is { PublishedVersion: 2 } &&
                secondBody.PublishedVersionId != publishedBody?.PublishedVersionId);

            using var scope = factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var flowId = await db.Flows.Where(flow => flow.Code == "FLOW_MANAGED")
                .Select(flow => flow.Id).SingleAsync();
            var versions = await db.FlowVersions.Where(item => item.FlowId == flowId)
                .OrderBy(item => item.Version).ToArrayAsync();
            check("旧发布版内容保持不可变且管理审计不存完整 JSON", versions.Length == 2 &&
                versions[0].DefinitionJson.Contains("受管理流程", StringComparison.Ordinal) &&
                versions[1].DefinitionJson.Contains("受管理流程第二稿", StringComparison.Ordinal) &&
                await db.FlowManagementAudits.CountAsync() == 4 &&
                await db.FlowManagementAudits.AllAsync(item => item.ContentSha256 != null &&
                    item.ContentSha256.Length == 64));

            var enabledPath = ApiRoutes.AdminFlowEnabled.Replace("{code}", "FLOW_MANAGED",
                StringComparison.Ordinal);
            using (var disabled = await SendAsync(client, HttpMethod.Put, enabledPath, manager,
                new AdminFlowEnabledRequest
                {
                    ExpectedEnabled = true,
                    Enabled = false,
                    Reason = "试点暂停新单发起",
                }))
            {
                var body = await disabled.Content.ReadFromJsonAsync<AdminFlowResponse>();
                check("停用只切断新发起且保留当前发布版", disabled.IsSuccessStatusCode &&
                    body is { IsEnabled: false, PublishedVersion: 2 } &&
                    await db.FlowVersions.CountAsync(item => item.FlowId == flowId) == 2);
            }

            var catalog = scope.ServiceProvider.GetRequiredService<IFlowCatalogRepository>();
            check("停用流程不再进入开放发起目录", await catalog.LoadPublishedAsync("FLOW_MANAGED") is null);
            using (var staleEnabled = await SendAsync(client, HttpMethod.Put, enabledPath, manager,
                new AdminFlowEnabledRequest
                {
                    ExpectedEnabled = true,
                    Enabled = true,
                    Reason = "使用旧启用状态",
                }))
            {
                check("启停操作也拒绝过期状态", staleEnabled.StatusCode == HttpStatusCode.Conflict);
            }

            using (var same = await SendAsync(client, HttpMethod.Put, enabledPath, manager,
                new AdminFlowEnabledRequest
                {
                    ExpectedEnabled = false,
                    Enabled = false,
                    Reason = "确认仍暂停新单",
                }))
            {
                check("重复停用为幂等读取且不增加审计", same.IsSuccessStatusCode &&
                    await db.FlowManagementAudits.CountAsync() == 5);
            }

            using (var enabled = await SendAsync(client, HttpMethod.Put, enabledPath, manager,
                new AdminFlowEnabledRequest
                {
                    ExpectedEnabled = false,
                    Enabled = true,
                    Reason = "恢复试点新单发起",
                }))
            {
                var body = await enabled.Content.ReadFromJsonAsync<AdminFlowResponse>();
                check("恢复新发起沿用原发布版并保留停启用审计", enabled.IsSuccessStatusCode &&
                    body is { IsEnabled: true, PublishedVersion: 2 } &&
                    await catalog.LoadPublishedAsync("FLOW_MANAGED") is not null &&
                    await db.FlowManagementAudits.CountAsync() == 6);
            }

            var oldDefinition = ReadSample(backendRoot);
            oldDefinition.Code = "LEGACY_MANAGED";
            oldDefinition.Name = "存量流程";
            db.Flows.Add(new FlowEntity
            {
                Id = "legacy-flow",
                Code = "LEGACY_MANAGED",
                Name = "存量流程",
                OwnerDepartmentId = "child",
                PublishedVersionId = "legacy-version",
            });
            db.FlowVersions.Add(new FlowVersionEntity
            {
                Id = "legacy-version",
                FlowId = "legacy-flow",
                Version = 1,
                SchemaVersion = 1,
                DefinitionJson = JsonSerializer.Serialize(oldDefinition, ApprovalJson.Options),
                GraphJson = "{}",
            });
            await db.SaveChangesAsync();

            var legacyPath = ApiRoutes.AdminFlowByCode.Replace("{code}", "LEGACY_MANAGED",
                StringComparison.Ordinal);
            using (var legacy = await SendAsync(client, HttpMethod.Get, legacyPath, manager))
            {
                var body = await legacy.Content.ReadFromJsonAsync<AdminFlowResponse>();
                check("存量已发布流程无草稿时显示修订零的可编辑快照",
                    legacy.IsSuccessStatusCode && body is { DraftRevision: 0 } &&
                    body.Draft?.Code == "LEGACY_MANAGED");
            }

            var legacyDraftPath = ApiRoutes.AdminFlowDraft.Replace("{code}", "LEGACY_MANAGED",
                StringComparison.Ordinal);
            using (var legacySave = await SendAsync(client, HttpMethod.Put, legacyDraftPath, manager,
                new AdminFlowDraftRequest
                {
                    ExpectedRevision = 0,
                    Definition = oldDefinition,
                    OwnerDepartmentId = "child",
                    Reason = "恢复存量流程草稿",
                }))
            {
                var body = await legacySave.Content.ReadFromJsonAsync<AdminFlowResponse>();
                check("首次编辑存量流程建立修订一草稿且发布版仍不变",
                    legacySave.IsSuccessStatusCode && body is { DraftRevision: 1, PublishedVersion: 1 });
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 创建组织角色、直属部门成员和可正常登录的账号。
    /// </summary>
    /// <param name="factory">真实服务宿主。</param>
    /// <returns>异步操作。</returns>
    private static async Task SeedAsync(ServerFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Departments.AddRange(
                new DepartmentEntity { Id = "parent", Code = "P", Name = "上级部门" },
                new DepartmentEntity { Id = "child", Code = "C", Name = "直属部门", ParentId = "parent" });
            db.People.AddRange(
                new PersonEntity { Id = "admin", EmployeeNumber = "A001", Name = "组织管理员" },
                new PersonEntity { Id = "manager", EmployeeNumber = "M001", Name = "部门流程管理员" },
                new PersonEntity { Id = "parent-manager", EmployeeNumber = "P001", Name = "上级流程管理员" },
                new PersonEntity { Id = "unassigned-manager", EmployeeNumber = "U001", Name = "无部门流程管理员" },
                new PersonEntity { Id = "viewer", EmployeeNumber = "V001", Name = "普通人员" });
            db.OrganizationRoles.AddRange(
                new OrganizationRoleEntity { Code = "organization-admin", Name = "组织管理员" },
                new OrganizationRoleEntity { Code = "flow-admin", Name = "流程管理员" });
            db.PersonRoles.AddRange(
                new PersonRoleEntity { PersonId = "admin", RoleCode = "organization-admin" },
                new PersonRoleEntity { PersonId = "manager", RoleCode = "flow-admin" },
                new PersonRoleEntity { PersonId = "parent-manager", RoleCode = "flow-admin" });
            db.PersonRoles.Add(new PersonRoleEntity
            {
                PersonId = "unassigned-manager",
                RoleCode = "flow-admin",
            });
            db.PersonDepartments.AddRange(
                new PersonDepartmentEntity { PersonId = "manager", DepartmentId = "child", IsPrimary = true },
                new PersonDepartmentEntity { PersonId = "parent-manager", DepartmentId = "parent", IsPrimary = true });
            await db.SaveChangesAsync();
        }

        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
        await ProvisionAsync(identity, "admin", "admin-password-new");
        await ProvisionAsync(identity, "manager", "manager-password-new");
        await ProvisionAsync(identity, "parent-manager", "parent-password-new");
        await ProvisionAsync(identity, "unassigned-manager", "unassigned-password-new");
        await ProvisionAsync(identity, "viewer", "viewer-password-new");
    }

    /// <summary>
    /// 为测试人员开通账号并完成首次改密。
    /// </summary>
    /// <param name="identity">身份用例。</param>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="password">日常登录密码。</param>
    /// <returns>异步操作。</returns>
    private static async Task ProvisionAsync(IIdentityAppService identity, string personId, string password)
    {
        await identity.ProvisionAccountAsync(personId, "initial-password-for-check");
        await identity.ChangePasswordAsync(personId, "initial-password-for-check", password);
    }

    /// <summary>
    /// 读取已有的可发布自由画布样例。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <returns>样例流程定义。</returns>
    private static FlowDefinition ReadSample(string backendRoot)
    {
        var file = Directory.GetFiles(Path.Combine(backendRoot, "samples", "flows"), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal).First();
        return JsonSerializer.Deserialize<FlowDefinition>(File.ReadAllText(file), ApprovalJson.Options)!;
    }

    /// <summary>
    /// 使用本地账号密码取得真实 JWT。
    /// </summary>
    /// <param name="client">HTTP 客户端。</param>
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
    /// 以真实访问令牌发送管理请求。
    /// </summary>
    /// <param name="client">HTTP 客户端。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">请求路由。</param>
    /// <param name="token">访问令牌。</param>
    /// <param name="body">可选 JSON 正文。</param>
    /// <returns>未释放的 HTTP 响应。</returns>
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
