using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Files;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 中心查询的数据库夹具；运行时参与者写入由独立的持久化场景验证。
/// </summary>
internal static class CenterChecksFixture
{
    /// <summary>
    /// 创建两应用、多个参与关系和真实账号，返回真实签发的访问令牌。
    /// </summary>
    /// <param name="services">真实宿主服务。</param>
    /// <returns>本地、嵌入、首次登录、无关管理员令牌及测试 API Key。</returns>
    public static async Task<(string Local, string Embed, string First, string Stranger, string ApiKey)> SeedAsync(
        IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<ClientAppAppService>();
        var app = await apps.CreateAsync(new CreateClientAppInput { Code = "one", Name = "应用一" });
        await apps.CreateAsync(new CreateClientAppInput { Code = "two", Name = "应用二" });
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var id in new[] { "viewer", "stranger", "first", "other" })
            {
                db.People.Add(new PersonEntity { Id = id, EmployeeNumber = id, Name = "测试人员" });
            }
            db.OrganizationRoles.Add(new OrganizationRoleEntity { Code = "organization-admin", Name = "组织管理员" });
            db.PersonRoles.Add(new PersonRoleEntity { PersonId = "stranger", RoleCode = "organization-admin" });
            db.ExternalPersonBindings.Add(new ExternalPersonBindingEntity { ClientAppCode = "one", ExternalUserId = "external", PersonId = "viewer" });
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
            graph.IsConfidential = true;
            var node = graph.Nodes.First(item => item.Type == FlowNodeTypes.Approval);
            node.FieldPermissions =
            [
                new FlowFieldPermission { FieldKey = "secret", Access = FlowFieldAccess.Hidden },
                new FlowFieldPermission { FieldKey = "lines.cost", Access = FlowFieldAccess.Hidden },
                new FlowFieldPermission { FieldKey = "secretReceipt", Access = FlowFieldAccess.Hidden },
                new FlowFieldPermission { FieldKey = "lines.costReceipt", Access = FlowFieldAccess.Hidden },
            ];
            var definition = new FlowDefinition
            {
                Code = "CENTER",
                Name = "保密测试",
                Data = new FlowDataDefinition
                {
                    SummaryFields = ["amount", "secret", "lines"],
                    Fields =
                    [
                        new FlowFieldDefinition { Key = "amount", Name = "金额", Type = FlowFieldTypes.Money },
                        new FlowFieldDefinition { Key = "secret", Name = "保密字段" },
                        new FlowFieldDefinition { Key = "receipt", Name = "票据", Type = FlowFieldTypes.Attachment },
                        new FlowFieldDefinition { Key = "secretReceipt", Name = "隐藏票据", Type = FlowFieldTypes.Attachment },
                        new FlowFieldDefinition { Key = "lines", Name = "明细", Type = FlowFieldTypes.DetailTable,
                            Children = [new FlowFieldDefinition { Key = "qty", Name = "数量" },
                                new FlowFieldDefinition { Key = "cost", Name = "成本" },
                                new FlowFieldDefinition { Key = "receipt", Name = "行票据", Type = FlowFieldTypes.Attachment },
                                new FlowFieldDefinition { Key = "costReceipt", Name = "隐藏行票据", Type = FlowFieldTypes.Attachment }] },
                    ],
                },
                DetailLayout = new FlowDetailLayout
                {
                    Mode = "custom",
                    Groups =
                    [
                        new FlowLayoutGroup { Title = "基本信息", Columns = 2, Items =
                            [new FlowLayoutItem { FieldKey = "amount", ShowMoneyUppercase = true },
                                new FlowLayoutItem { FieldKey = "receipt" }] },
                        new FlowLayoutGroup { Title = "隐藏成本分组", Columns = 3, Items =
                            [new FlowLayoutItem { FieldKey = "lines", ColumnSpan = 3,
                                DetailColumnWidths = new Dictionary<string, int> { ["qty"] = 35, ["cost"] = 30, ["receipt"] = 65 } },
                                new FlowLayoutItem { FieldKey = "secret" },
                                new FlowLayoutItem { FieldKey = "secretReceipt" }] },
                        new FlowLayoutGroup { Title = "全部隐藏", Items =
                            [new FlowLayoutItem { FieldKey = "secret" }] },
                    ],
                },
            };
            db.Flows.Add(new FlowEntity { Id = "flow", Code = "CENTER", Name = "测试流程", AdministratorId = "stranger", PublishedVersionId = "version" });
            db.FlowVersions.Add(new FlowVersionEntity
            {
                Id = "version",
                FlowId = "flow",
                Version = 1,
                SchemaVersion = 1,
                DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
                GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
            });
            // 最新发布版本更名不应改写既有审批单已锁定版本的字段名称。
            definition.Data.Fields[0].Name = "新版金额";
            definition.DetailLayout.Groups[0].Title = "新版分组";
            db.FlowVersions.Add(new FlowVersionEntity
            {
                Id = "version-new",
                FlowId = "flow",
                Version = 2,
                SchemaVersion = 1,
                DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
                GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
            });
            db.Flows.Local.Single(item => item.Id == "flow").PublishedVersionId = "version-new";
            AddInstance(db, "a-todo", "one", node.Id, "other", "viewer", EngineTaskStatuses.Pending, true);
            AddInstance(db, "b-cross", "two", node.Id, "other", "viewer", EngineTaskStatuses.Pending);
            AddInstance(db, "c-done", "one", node.Id, "other", "other", EngineTaskStatuses.Approved);
            AddInstance(db, "d-copy", "one", node.Id, "other");
            AddInstance(db, "e-started", "one", node.Id, "viewer");
            AddInstance(db, "f-submitted", "one", node.Id, "other");
            db.Instances.Local.Single(item => item.Id == "f-submitted").SubmittedById = "viewer";
            AddInstance(db, "g-forbidden", "one", node.Id, "other", "other", EngineTaskStatuses.Pending);
            AddInstance(db, "h-queued", "one", node.Id, "other", "viewer", EngineTaskStatuses.Queued);
            AddInstance(db, "i-cancelled", "one", node.Id, "other", "viewer", EngineTaskStatuses.Cancelled);
            AddInstance(db, "j-delegated", "one", node.Id, "other", "other", EngineTaskStatuses.Pending);
            db.ApprovalTasks.Local.Single(item => item.InstanceId == "j-delegated").DelegatedForId = "viewer";
            AddInstance(db, "k-history", "one", node.Id, "other", "viewer", EngineTaskStatuses.Queued);
            foreach (var (id, instanceId) in new[]
            {
                ("file-visible", "a-todo"), ("file-row", "a-todo"), ("file-hidden", "a-todo"),
                ("file-hidden-row", "a-todo"), ("file-foreign", "b-cross"),
            })
            {
                db.StoredFiles.Add(new StoredFileEntity
                {
                    Id = id,
                    FileName = id + ".pdf",
                    Size = 42,
                    ContentType = "application/pdf",
                    Sha256 = new string('0', 64),
                    StorageKind = "local",
                    StorageKey = id,
                    InstanceId = instanceId,
                    UploadedById = "other",
                    UploadedAt = DateTimeOffset.UtcNow,
                });
            }
            db.ApprovalTasks.Local.Single(item => item.InstanceId == "k-history").NodeId = graph.Nodes.Last(item => item.Type == FlowNodeTypes.Approval).Id;
            // 同一审批单多个本人待办不能导致列表重复或总数膨胀。
            db.ApprovalTasks.Add(new ApprovalTaskEntity { Id = "a-second", InstanceId = "a-todo", NodeId = node.Id, AssigneeId = "viewer", Status = EngineTaskStatuses.Pending });
            // 这张待办的运行快照与任务投影保持一致，供中心操作许可场景验证。
            var writable = db.Instances.Local.Single(item => item.Id == "a-todo");
            writable.LastEventSequence = 2;
            writable.StateJson = JsonSerializer.Serialize(new EngineState
            {
                InstanceId = "a-todo",
                Status = EngineInstanceStatuses.Running,
                Tokens = [new EngineToken { Id = "a-todo-token", NodeId = node.Id, WaitingFor = "task" }],
                Tasks =
                [
                    new EngineTask { Id = "a-todo-task", TokenId = "a-todo-token", NodeId = node.Id,
                        AssigneeId = "viewer", Status = EngineTaskStatuses.Pending,
                        CompletionKind = EngineCommandKinds.Approve, CanReject = true },
                    new EngineTask { Id = "a-second", TokenId = "a-todo-token", NodeId = node.Id,
                        AssigneeId = "viewer", Status = EngineTaskStatuses.Pending,
                        CompletionKind = EngineCommandKinds.Approve, CanReject = true },
                ],
            }, ApprovalJson.Options);
            foreach (var (id, kind) in new[] { ("c-done", InstanceParticipantKinds.TaskActor), ("d-copy", InstanceParticipantKinds.CopyRecipient), ("k-history", InstanceParticipantKinds.TaskActor) })
            {
                db.InstanceParticipants.Add(new InstanceParticipantEntity { InstanceId = id, PersonId = "viewer", Kind = kind, EventId = id + "-event", GrantedAt = DateTimeOffset.UtcNow });
            }
            db.OperationEvents.Add(new OperationEventEntity
            {
                Id = "private-draft",
                InstanceId = "a-todo",
                Sequence = 2,
                Kind = "opinionDraftSaved",
                OccurredAt = DateTimeOffset.UtcNow,
                PayloadJson = JsonSerializer.Serialize(new EngineEvent
                {
                    Kind = "opinionDraftSaved",
                    Audit = new OperationAudit { ActorId = "other", Opinion = "private-unsent-draft" },
                }, ApprovalJson.Options),
            });
            await db.SaveChangesAsync();
        }
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
        foreach (var id in new[] { "viewer", "stranger", "first" })
        {
            await identity.ProvisionAccountAsync(id, "temporary-test-password");
            if (id != "first")
            {
                await identity.ChangePasswordAsync(id, "temporary-test-password", "changed-test-password");
            }
        }
        var local = await identity.LoginAsync("viewer", "changed-test-password");
        var stranger = await identity.LoginAsync("stranger", "changed-test-password");
        var first = await identity.LoginAsync("first", "temporary-test-password");
        var embed = await identity.CreateEmbedTokenAsync("one", "external", "viewer", "#/center");
        var exchanged = await identity.ExchangeEmbedTokenAsync(embed.Token);
        return (local.AccessToken, exchanged.AccessToken, first.AccessToken, stranger.AccessToken, app.ApiKey);
    }

    /// <summary>
    /// 添加具有规范快照的查询测试单据和可选任务。
    /// </summary>
    /// <param name="db">测试数据库。</param>
    /// <param name="id">审批单 ID。</param>
    /// <param name="app">来源应用。</param>
    /// <param name="node">节点 ID。</param>
    /// <param name="initiator">发起人。</param>
    /// <param name="assignee">任务指派人员；为空则不建任务。</param>
    /// <param name="taskStatus">任务状态。</param>
    /// <param name="urgent">是否加急。</param>
    private static void AddInstance(ApprovalDbContext db, string id, string app, string node, string initiator,
        string? assignee = null, string taskStatus = "", bool urgent = false)
    {
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);
        db.Instances.Add(new InstanceEntity
        {
            Id = id,
            SerialNo = "SP-" + id,
            FlowId = "flow",
            FlowVersionId = "version",
            FlowCode = "CENTER",
            BusinessKey = "B-" + id,
            Title = "测试 " + id,
            InitiatorId = initiator,
            ClientAppCode = app,
            Status = EngineInstanceStatuses.Running,
            StartedAt = now,
            IsUrgent = urgent,
            ApprovalDataJson = "{\"amount\":12,\"secret\":\"hidden-business-secret\",\"receipt\":[\"file-visible\",\"file-foreign\"],\"secretReceipt\":\"file-hidden\",\"extra\":\"unregistered-secret\",\"lines\":[{\"qty\":2,\"cost\":100,\"receipt\":\"file-row\",\"costReceipt\":\"file-hidden-row\",\"extra\":\"unregistered-column\"}]}",
            StateJson = JsonSerializer.Serialize(new EngineState { InstanceId = id, Tokens = [new EngineToken { Id = id + "-token", NodeId = node }] }, ApprovalJson.Options),
        });
        if (assignee is not null)
        {
            db.ApprovalTasks.Add(new ApprovalTaskEntity { Id = id + "-task", InstanceId = id, NodeId = node, AssigneeId = assignee, Status = taskStatus });
        }
        db.OperationEvents.Add(new OperationEventEntity
        {
            Id = id + "-event",
            InstanceId = id,
            Sequence = 1,
            Kind = "taskCompleted",
            OccurredAt = now,
            PayloadJson = JsonSerializer.Serialize(new EngineEvent
            {
                Kind = "taskCompleted",
                NodeId = node,
                Audit = new OperationAudit
                {
                    ActorId = "viewer",
                    Opinion = "已核对",
                    FieldValues = JsonSerializer.SerializeToElement(new { secret = "hidden-event-secret" })
                },
            }, ApprovalJson.Options),
        });
    }
}
