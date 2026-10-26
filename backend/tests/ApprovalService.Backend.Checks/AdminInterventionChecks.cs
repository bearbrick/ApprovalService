using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 真实 HTTP、JWT 和 SQLite 下的管理员干预闭环。
/// </summary>
internal static class AdminInterventionChecks
{
    /// <summary>
    /// 验证授权、保密隔离、幂等改派、并发、跳转、终止与审计。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <param name="check">结果记录器。</param>
    /// <returns>验证完成。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            await using var server = new ServerFactory(backendRoot, directory,
                new Dictionary<string, string?>
                {
                    ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
                });
            using var http = server.CreateClient();
            var tokens = await CenterChecksFixture.SeedAsync(server.Services);
            using var scope = server.Services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
            var runtime = scope.ServiceProvider.GetRequiredService<IApprovalRuntimeAppService>();
            await SeedAsync(factory, runtime);
            using var anonymous = await http.GetAsync(AdminInterventionRoutes.Instances);
            using var embedded = await SendAsync(http, tokens.Embed, null);
            using var plain = await SendAsync(http, tokens.Local, null);
            check("运行干预拒绝匿名、嵌入会话和普通人员", anonymous.StatusCode == HttpStatusCode.Unauthorized &&
                embedded.StatusCode == HttpStatusCode.Forbidden && plain.StatusCode == HttpStatusCode.Forbidden);
            using var adminList = await SendAsync(http, tokens.Stranger, null);
            var rows = await adminList.Content.ReadFromJsonAsync<List<AdminInterventionResponse>>();
            check("组织管理员可读普通在途单据但不能看到无参与关系保密单", rows?.Count == 1 &&
                rows[0].Id == "intervention");
            using var secret = await SendAsync(http, tokens.Stranger, "a-todo");
            check("保密单直接定位同样不扩张管理员权限", secret.StatusCode == HttpStatusCode.NotFound);
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Departments.Add(new DepartmentEntity { Id = "dept", Name = "本部门" });
                db.OrganizationRoles.Add(new OrganizationRoleEntity { Code = "flow-admin", Name = "流程管理员" });
                db.PersonRoles.Add(new PersonRoleEntity { PersonId = "viewer", RoleCode = "flow-admin" });
                db.PersonDepartments.Add(new PersonDepartmentEntity { PersonId = "viewer", DepartmentId = "dept" });
                (await db.Flows.SingleAsync(item => item.Id == "intervention-flow")).OwnerDepartmentId = "dept";
                (await db.Flows.SingleAsync(item => item.Id == "flow")).OwnerDepartmentId = "dept";
                await db.SaveChangesAsync();
            }
            using var participant = await SendAsync(http, tokens.Local, "a-todo");
            using var managed = await SendAsync(http, tokens.Local, "intervention");
            var detail = (await managed.Content.ReadFromJsonAsync<AdminInterventionResponse>())!;
            check("直属部门流程管理员可管理本部门普通单和本人参与的保密单",
                participant.IsSuccessStatusCode && managed.IsSuccessStatusCode);
            var command = new AdminInterventionRequest
            {
                Action = "reassign",
                ExpectedVersion = detail.Version,
                TaskId = detail.Tasks[0].Id,
                TargetPersonId = "first",
                Reason = "出差交给同事",
            };
            using var missingId = await SendAsync(http, tokens.Local, "intervention", command, null);
            command.Reason = "短";
            using var missingReason = await SendAsync(http, tokens.Local, "intervention", command, "reason");
            command.Reason = "出差交给同事";
            command.TargetPersonId = "missing";
            using var missingPerson = await SendAsync(http, tokens.Local, "intervention", command, "missing-person");
            command.TargetPersonId = "first";
            using var changed = await SendAsync(http, tokens.Local, "intervention", command, "change-once");
            using var replay = await SendAsync(http, tokens.Local, "intervention", command, "change-once");
            using var stale = await SendAsync(http, tokens.Local, "intervention", command, "stale");
            check("干预要求稳定请求 ID、原因和有效接手人", missingId.StatusCode == HttpStatusCode.BadRequest &&
                missingReason.StatusCode == HttpStatusCode.BadRequest && missingPerson.StatusCode == HttpStatusCode.BadRequest);
            check("管理员改派真实生效、相同请求幂等且过期修订拒绝",
                changed.StatusCode == HttpStatusCode.NoContent && replay.StatusCode == HttpStatusCode.NoContent &&
                stale.StatusCode == HttpStatusCode.Conflict);
            using var afterChange = await SendAsync(http, tokens.Local, "intervention");
            detail = (await afterChange.Content.ReadFromJsonAsync<AdminInterventionResponse>())!;
            check("改派保持锁定流程版本并更新当前任务", detail.FlowVersionId == "intervention-version" &&
                detail.Tasks[0].AssigneeId == "first");
            command = new AdminInterventionRequest
            {
                Action = "jump",
                ExpectedVersion = detail.Version,
                TargetNodeId = "end",
                Reason = "调整审批路线",
            };
            using var forbiddenNode = await SendAsync(http, tokens.Local, "intervention", command, "bad-jump");
            command.TargetNodeId = "n2";
            using var jump = await SendAsync(http, tokens.Local, "intervention", command, "jump");
            check("跳转仅接受锁定发布版人工节点", forbiddenNode.StatusCode == HttpStatusCode.BadRequest &&
                jump.StatusCode == HttpStatusCode.NoContent);
            using var afterJump = await SendAsync(http, tokens.Local, "intervention");
            detail = (await afterJump.Content.ReadFromJsonAsync<AdminInterventionResponse>())!;
            check("跳转取消原待办并生成目标节点待办", detail.Tasks.Count == 1 && detail.Tasks[0].NodeId == "n2");
            command = new AdminInterventionRequest
            {
                Action = "terminate",
                ExpectedVersion = detail.Version,
                Reason = "业务取消审批",
            };
            using var terminate = await SendAsync(http, tokens.Local, "intervention", command, "terminate");
            using var terminateReplay = await SendAsync(http, tokens.Local, "intervention", command, "terminate");
            await using var verify = await factory.CreateDbContextAsync();
            var instance = await verify.Instances.SingleAsync(item => item.Id == "intervention");
            var events = await verify.OperationEvents.Where(item => item.InstanceId == instance.Id).ToListAsync();
            check("终止幂等并取消全部未处理任务且保留原因和事件链",
                terminate.StatusCode == HttpStatusCode.NoContent && terminateReplay.StatusCode == HttpStatusCode.NoContent &&
                instance.Status == EngineInstanceStatuses.Terminated &&
                !await verify.ApprovalTasks.AnyAsync(item => item.InstanceId == instance.Id && item.Status == EngineTaskStatuses.Pending) &&
                events.Count(item => item.Kind == "taskReassigned") == 1 &&
                events.Any(item => item.PayloadJson.Contains("业务取消审批", StringComparison.Ordinal)) &&
                await runtime.VerifyEventChainAsync(instance.Id));
            verify.PersonRoles.Remove(await verify.PersonRoles.SingleAsync(item => item.PersonId == "viewer"));
            await verify.SaveChangesAsync();
            using var revoked = await SendAsync(http, tokens.Local, "intervention");
            check("撤销管理角色后旧 JWT 无法继续读取干预详情", revoked.StatusCode == HttpStatusCode.Forbidden);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 建立经真实运行入口发起的普通审批，保证事件链完整。
    /// </summary>
    /// <param name="factory">数据库工厂。</param>
    /// <param name="runtime">统一运行入口。</param>
    /// <returns>建立完成。</returns>
    private static async Task SeedAsync(IDbContextFactory<ApprovalDbContext> factory,
        IApprovalRuntimeAppService runtime)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
        foreach (var node in graph.Nodes.Where(item => item.Type == FlowNodeTypes.Approval))
        {
            node.Name = node.Id;
            node.Assignee = new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "viewer" };
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Flows.Add(new FlowEntity
            {
                Id = "intervention-flow",
                Code = "INTERVENTION",
                Name = "干预测试",
                AdministratorId = "stranger",
                PublishedVersionId = "intervention-version"
            });
            db.FlowVersions.Add(new FlowVersionEntity
            {
                Id = "intervention-version",
                FlowId = "intervention-flow",
                Version = 1,
                SchemaVersion = 1,
                GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                DefinitionJson = JsonSerializer.Serialize(new FlowDefinition { Code = "INTERVENTION", Name = "干预测试" },
                    ApprovalJson.Options),
            });
            await db.SaveChangesAsync();
        }
        var context = EngineScenarioChecks.Context(graph);
        context.Assignees = new Dictionary<string, string>(StringComparer.Ordinal) { ["n1"] = "viewer", ["n2"] = "viewer" };
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            StartInstance = new InstanceEntity
            {
                Id = "intervention",
                FlowCode = "INTERVENTION",
                FlowVersionId = "intervention-version",
                ClientAppCode = "one",
                BusinessKey = "intervention",
                InitiatorId = "other",
                Title = "运行干预测试"
            },
            Command = new EngineCommand { Kind = EngineCommandKinds.Start, InstanceId = "intervention", RequestId = "start" },
            Context = context,
        });
    }

    /// <summary>
    /// 请求列表、详情或带固定请求 ID 的命令。
    /// </summary>
    /// <param name="http">HTTP 客户端。</param>
    /// <param name="token">访问令牌。</param>
    /// <param name="id">实例 ID；空值查询列表。</param>
    /// <param name="body">可选命令。</param>
    /// <param name="requestId">可选固定请求 ID。</param>
    /// <returns>由调用者释放的响应。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, string? id,
        AdminInterventionRequest? body = null, string? requestId = null)
    {
        var path = id is null ? AdminInterventionRoutes.Instances :
            (body is null ? AdminInterventionRoutes.Instance : AdminInterventionRoutes.Commands)
                .Replace("{id}", Uri.EscapeDataString(id), StringComparison.Ordinal);
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (requestId is not null)
        {
            request.Headers.Add(HeaderNames.RequestId, requestId);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await http.SendAsync(request);
    }
}
