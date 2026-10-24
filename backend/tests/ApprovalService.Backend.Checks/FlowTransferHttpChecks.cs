using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
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

            using var secondPublish = await SendAsync(client, HttpMethod.Post, publishPath, admin,
                new AdminFlowPublishRequest
                {
                    ExpectedRevision = 2,
                    ExpectedPublishedVersionId = firstVersion?.PublishedVersionId,
                    Reason = "发布修改后第二版",
                });
            var secondVersion = await secondPublish.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("第二稿发布为追加的不可变版本", secondPublish.IsSuccessStatusCode &&
                secondVersion is { PublishedVersion: 2 } &&
                secondVersion.PublishedVersionId != firstVersion?.PublishedVersionId);

            var versionsPath = ApiRoutes.AdminFlowVersions.Replace("{code}", definition.Code,
                StringComparison.Ordinal);
            using (var versions = await SendAsync(client, HttpMethod.Get, versionsPath, admin))
            {
                var body = await versions.Content.ReadFromJsonAsync<AdminFlowVersionResponse[]>();
                check("版本历史只返回倒序摘要与当前标记", versions.IsSuccessStatusCode &&
                    body is { Length: 2 } && body[0].Version == 2 && body[0].IsCurrent &&
                    body[1].Version == 1 && !body[1].IsCurrent);
            }

            using (var outsideVersions = await SendAsync(client, HttpMethod.Get, versionsPath, manager))
            {
                check("其他部门流程管理员不能读取版本历史", outsideVersions.StatusCode == HttpStatusCode.NotFound);
            }

            var restorePath = ApiRoutes.AdminFlowVersionRestore
                .Replace("{code}", definition.Code, StringComparison.Ordinal)
                .Replace("{version}", "1", StringComparison.Ordinal);
            using (var staleRestore = await SendAsync(client, HttpMethod.Post, restorePath, admin,
                new AdminFlowVersionRestoreRequest
                {
                    ExpectedRevision = 1,
                    ExpectedPublishedVersionId = secondVersion?.PublishedVersionId,
                    Reason = "使用旧草稿修订恢复",
                }))
            {
                check("恢复旧版拒绝过期草稿修订", staleRestore.StatusCode == HttpStatusCode.Conflict);
            }

            using (var stalePublished = await SendAsync(client, HttpMethod.Post, restorePath, admin,
                new AdminFlowVersionRestoreRequest
                {
                    ExpectedRevision = 2,
                    ExpectedPublishedVersionId = firstVersion?.PublishedVersionId,
                    Reason = "使用旧发布指针恢复",
                }))
            {
                check("恢复旧版拒绝过期当前发布版", stalePublished.StatusCode == HttpStatusCode.Conflict);
            }

            using var restored = await SendAsync(client, HttpMethod.Post, restorePath, admin,
                new AdminFlowVersionRestoreRequest
                {
                    ExpectedRevision = 2,
                    ExpectedPublishedVersionId = secondVersion?.PublishedVersionId,
                    Reason = "将第一版复制到草稿",
                });
            var restoredBody = await restored.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("恢复第一版仅生成修订三草稿并保留当前第二版", restored.IsSuccessStatusCode &&
                restoredBody is { DraftRevision: 3, PublishedVersion: 2 } &&
                restoredBody.Draft?.Name == "导入导出流程" &&
                restoredBody.PublishedVersionId == secondVersion?.PublishedVersionId);

            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                check("旧版恢复审计包含来源版本与摘要而不改旧版本", await db.FlowManagementAudits
                    .AnyAsync(item => item.Action == "restore-version" && item.PublishedVersion == 1 &&
                        item.DraftRevision == 3 && item.ContentSha256 != null &&
                        item.ContentSha256.Length == 64) &&
                    await db.FlowVersions.CountAsync() == 2);
            }

            using var republished = await SendAsync(client, HttpMethod.Post, publishPath, admin,
                new AdminFlowPublishRequest
                {
                    ExpectedRevision = 3,
                    ExpectedPublishedVersionId = secondVersion?.PublishedVersionId,
                    Reason = "把恢复草稿发布为第三版",
                });
            var thirdVersion = await republished.Content.ReadFromJsonAsync<AdminFlowResponse>();
            check("恢复内容重新发布时生成新第三版", republished.IsSuccessStatusCode &&
                thirdVersion is { PublishedVersion: 3 } &&
                thirdVersion.PublishedVersionId != firstVersion?.PublishedVersionId &&
                thirdVersion.PublishedVersionId != secondVersion?.PublishedVersionId);

            var legacyDefinition = ReadSample(backendRoot);
            legacyDefinition.Code = "LEGACY_TRANSFER";
            legacyDefinition.Name = "存量待恢复流程";
            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                db.Flows.Add(new FlowEntity
                {
                    Id = "legacy-transfer-flow",
                    Code = legacyDefinition.Code,
                    Name = legacyDefinition.Name,
                    OwnerDepartmentId = "child",
                    PublishedVersionId = "legacy-transfer-version",
                });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "legacy-transfer-version",
                    FlowId = "legacy-transfer-flow",
                    Version = 1,
                    SchemaVersion = legacyDefinition.SchemaVersion,
                    DefinitionJson = JsonSerializer.Serialize(legacyDefinition, ApprovalJson.Options),
                    GraphJson = "{}",
                });
                await db.SaveChangesAsync();
            }

            var legacyRestorePath = ApiRoutes.AdminFlowVersionRestore
                .Replace("{code}", legacyDefinition.Code, StringComparison.Ordinal)
                .Replace("{version}", "1", StringComparison.Ordinal);
            using (var legacyRestored = await SendAsync(client, HttpMethod.Post, legacyRestorePath, admin,
                new AdminFlowVersionRestoreRequest
                {
                    ExpectedRevision = 0,
                    ExpectedPublishedVersionId = "legacy-transfer-version",
                    Reason = "从存量发布版建立草稿",
                }))
            {
                var body = await legacyRestored.Content.ReadFromJsonAsync<AdminFlowResponse>();
                check("存量无草稿流程可按修订零恢复第一份实体草稿", legacyRestored.IsSuccessStatusCode &&
                    body is { DraftRevision: 1, PublishedVersion: 1 } &&
                    body.Draft?.Code == legacyDefinition.Code);
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
