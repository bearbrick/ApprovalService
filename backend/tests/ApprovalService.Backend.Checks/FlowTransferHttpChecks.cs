using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Engine.Definitions;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 HTTP、JWT 和 SQLite 验证兼容 BPMN 的草稿导入与版本导出。
/// </summary>
internal static class FlowTransferHttpChecks
{
    /// <summary>
    /// 执行文件预检、冲突、授权、修订和往返场景。
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
            var admin = await LoginAsync(client, "A001", "admin-password-new");
            var manager = await LoginAsync(client, "M001", "manager-password-new");
            var outsider = await LoginAsync(client, "V001", "viewer-password-new");
            var definition = ReadSample(backendRoot);
            definition.Code = "FLOW_TRANSFER";
            definition.Name = "导入导出流程";
            var xml = BpmnFlowCodec.Export(definition);

            using (var anonymous = await client.PostAsJsonAsync(ApiRoutes.AdminFlowImportPreview,
                new AdminFlowImportPreviewRequest { BpmnXml = xml }))
            {
                check("匿名不能预检流程导入", anonymous.StatusCode == HttpStatusCode.Unauthorized);
            }

            using (var invalid = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlowImportPreview,
                admin, new AdminFlowImportPreviewRequest { BpmnXml = "<definitions />" }))
            {
                check("第三方 BPMN 文件被明确拒绝", invalid.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var preview = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlowImportPreview,
                admin, new AdminFlowImportPreviewRequest { BpmnXml = xml }))
            {
                var body = await preview.Content.ReadFromJsonAsync<AdminFlowImportPreviewResponse>();
                check("兼容文件预检返回流程标识与发布检查", preview.IsSuccessStatusCode &&
                    body is { Code: "FLOW_TRANSFER", HasCodeConflict: false, CanOverwrite: false } &&
                    body.NodeCount == definition.Canvas.Nodes.Count && body.PublishIssues.Count == 0);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                check("导入预检不创建流程或草稿", !await db.Flows.AnyAsync() &&
                    !await db.FlowDrafts.AnyAsync());
            }

            using var created = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlowImport, admin,
                new AdminFlowImportRequest
                {
                    BpmnXml = xml,
                    OwnerDepartmentId = "child",
                    Reason = "导入首次流程草稿",
                });
            var first = await created.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("首次导入仅建立修订一草稿", created.IsSuccessStatusCode &&
                first is { Code: "FLOW_TRANSFER", DraftRevision: 1, PublishedVersion: null });

            using (var conflict = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlowImport, admin,
                new AdminFlowImportRequest { BpmnXml = xml, Reason = "尝试同编码导入" }))
            {
                check("同编码导入默认冲突", conflict.StatusCode == HttpStatusCode.Conflict);
            }

            using (var preview = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlowImportPreview,
                admin, new AdminFlowImportPreviewRequest { BpmnXml = xml }))
            {
                var body = await preview.Content.ReadFromJsonAsync<AdminFlowImportPreviewResponse>();
                check("同编码预检报告当前草稿修订与覆盖能力", preview.IsSuccessStatusCode &&
                    body is { HasCodeConflict: true, CanOverwrite: true, ExistingDraftRevision: 1 });
            }

            using (var outsidePreview = await SendAsync(client, HttpMethod.Post,
                ApiRoutes.AdminFlowImportPreview, manager,
                new AdminFlowImportPreviewRequest { BpmnXml = xml }))
            {
                var body = await outsidePreview.Content.ReadFromJsonAsync<AdminFlowImportPreviewResponse>();
                check("其他直属部门管理员预检不获得覆盖能力与草稿修订", outsidePreview.IsSuccessStatusCode &&
                    body is { HasCodeConflict: true, CanOverwrite: false, ExistingDraftRevision: null });
            }

            using (var outsideImport = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlowImport,
                manager, new AdminFlowImportRequest
                {
                    BpmnXml = xml,
                    TargetCode = definition.Code,
                    ExpectedRevision = 1,
                    Reason = "尝试覆盖其他部门流程",
                }))
            {
                check("其他直属部门管理员不能覆盖目标草稿", outsideImport.StatusCode == HttpStatusCode.NotFound);
            }

            var exportPath = ApiRoutes.AdminFlowExport.Replace("{code}", "FLOW_TRANSFER",
                StringComparison.Ordinal);
            using (var forbidden = await SendAsync(client, HttpMethod.Get,
                exportPath + "?source=draft", outsider))
            {
                check("普通人员不能导出其他部门流程", forbidden.StatusCode == HttpStatusCode.NotFound);
            }

            using (var draft = await SendAsync(client, HttpMethod.Get, exportPath + "?source=draft", admin))
            {
                var returned = await draft.Content.ReadAsStringAsync();
                check("草稿导出保留完整兼容定义", draft.IsSuccessStatusCode &&
                    draft.Content.Headers.ContentType?.MediaType == "application/xml" &&
                    BpmnFlowCodec.Import(returned).Code == definition.Code);
            }

            var publishPath = ApiRoutes.AdminFlowPublish.Replace("{code}", "FLOW_TRANSFER",
                StringComparison.Ordinal);
            using var published = await SendAsync(client, HttpMethod.Post, publishPath, admin,
                new AdminFlowPublishRequest { ExpectedRevision = 1, Reason = "导入后发布第一版" });
            var firstVersion = await published.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("导入草稿须另行显式发布", published.IsSuccessStatusCode &&
                firstVersion is { PublishedVersion: 1 });

            definition.Name = "导入导出流程第二稿";
            var changedXml = BpmnFlowCodec.Export(definition);
            using (var stale = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlowImport, admin,
                new AdminFlowImportRequest
                {
                    BpmnXml = changedXml,
                    TargetCode = definition.Code,
                    ExpectedRevision = 0,
                    Reason = "使用过期修订覆盖",
                }))
            {
                check("显式覆盖拒绝过期修订", stale.StatusCode == HttpStatusCode.Conflict);
            }

            using var replaced = await SendAsync(client, HttpMethod.Post, ApiRoutes.AdminFlowImport, admin,
                new AdminFlowImportRequest
                {
                    BpmnXml = changedXml,
                    TargetCode = definition.Code,
                    ExpectedRevision = 1,
                    Reason = "覆盖当前草稿内容",
                });
            var second = await replaced.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("显式目标和预期修订只覆盖草稿", replaced.IsSuccessStatusCode &&
                second is { DraftRevision: 2, PublishedVersion: 1 } &&
                second.Draft?.Name == "导入导出流程第二稿");

            using (var version = await SendAsync(client, HttpMethod.Get,
                exportPath + "?source=published&version=1", admin))
            {
                var returned = await version.Content.ReadAsStringAsync();
                check("旧发布版导出仍是原始不可变内容", version.IsSuccessStatusCode &&
                    BpmnFlowCodec.Import(returned).Name == "导入导出流程");
            }

            using (var missingVersion = await SendAsync(client, HttpMethod.Get,
                exportPath + "?source=published", admin))
            {
                check("发布版导出必须显式选择版本", missingVersion.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                check("导入仅审计操作者原因版本与内容摘要", await db.FlowManagementAudits.CountAsync() == 3 &&
                    await db.FlowManagementAudits.AllAsync(item => item.ContentSha256 != null &&
                        item.ContentSha256.Length == 64));
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 建立在职管理员、普通人员和部门主数据。
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
                new DepartmentEntity { Id = "child", Code = "C", Name = "流程所属部门" },
                new DepartmentEntity { Id = "other", Code = "O", Name = "其他部门" });
            db.People.AddRange(
                new PersonEntity { Id = "admin", EmployeeNumber = "A001", Name = "组织管理员" },
                new PersonEntity { Id = "manager", EmployeeNumber = "M001", Name = "其他部门流程管理员" },
                new PersonEntity { Id = "viewer", EmployeeNumber = "V001", Name = "普通人员" });
            db.OrganizationRoles.Add(new OrganizationRoleEntity
            {
                Code = "organization-admin",
                Name = "组织管理员",
            });
            db.OrganizationRoles.Add(new OrganizationRoleEntity
            {
                Code = "flow-admin",
                Name = "流程管理员",
            });
            db.PersonRoles.Add(new PersonRoleEntity { PersonId = "admin", RoleCode = "organization-admin" });
            db.PersonRoles.Add(new PersonRoleEntity { PersonId = "manager", RoleCode = "flow-admin" });
            db.PersonDepartments.Add(new PersonDepartmentEntity
            {
                PersonId = "manager",
                DepartmentId = "other",
                IsPrimary = true,
            });
            await db.SaveChangesAsync();
        }

        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
        await identity.ProvisionAccountAsync("admin", "initial-password-for-check");
        await identity.ChangePasswordAsync("admin", "initial-password-for-check", "admin-password-new");
        await identity.ProvisionAccountAsync("manager", "initial-password-for-check");
        await identity.ChangePasswordAsync("manager", "initial-password-for-check", "manager-password-new");
        await identity.ProvisionAccountAsync("viewer", "initial-password-for-check");
        await identity.ChangePasswordAsync("viewer", "initial-password-for-check", "viewer-password-new");
    }

    /// <summary>
    /// 读取现有可发布流程样例。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <returns>样例定义。</returns>
    private static FlowDefinition ReadSample(string backendRoot)
    {
        var file = Directory.GetFiles(Path.Combine(backendRoot, "samples", "flows"), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal).First();
        return JsonSerializer.Deserialize<FlowDefinition>(File.ReadAllText(file), ApprovalJson.Options)!;
    }

    /// <summary>
    /// 以本地密码取得真实访问令牌。
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
    /// <param name="path">请求路径。</param>
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
