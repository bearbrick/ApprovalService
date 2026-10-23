using System.Text.Json;
using ApprovalService.Application.Notifications;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 核对事件通知和参与人权限随审批事务持久提交。
/// </summary>
internal static class NotificationRulesChecksIntegration
{
    /// <summary>
    /// 运行审批开始、完成和保密收件人场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "event-notifications.db")).Options;
            var factory = new NotificationContextFactory(options);
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
            graph.IsConfidential = true;
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.People.AddRange(
                    Person("requester", "requester@example.test"),
                    Person("user-n1", "approver@example.test"),
                    Person("historic", "historic@example.test"),
                    Person("outsider", "outsider@example.test"),
                    Person("inactive", "inactive@example.test", false));
                db.Flows.Add(new FlowEntity
                {
                    Id = "flow-notification",
                    Code = "NOTICE",
                    Name = "保密通知",
                    PublishedVersionId = "version-notification",
                });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "version-notification",
                    FlowId = "flow-notification",
                    Version = 1,
                    SchemaVersion = FlowSchema.CurrentVersion,
                    DefinitionJson = "{}",
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                await db.SaveChangesAsync();
            }

            var organization = new OrganizationRepository(factory);
            var service = new ApprovalRuntimeAppService(new ApprovalRuntimeRepository(factory),
                organization: organization);
            var startContext = EngineScenarioChecks.Context(graph);
            var started = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = EngineScenarioChecks.Command(EngineCommandKinds.Start, "notification-start"),
                Context = startContext,
                StartInstance = new InstanceEntity
                {
                    Id = "instance",
                    SerialNo = "SP-202610-000001",
                    FlowVersionId = "version-notification",
                    FlowCode = "NOTICE",
                    BusinessKey = "NOTICE-1",
                    Title = "采购申请",
                    InitiatorId = "requester",
                    ClientAppCode = "demo",
                },
            });
            var taskId = started.State.Tasks.Single().Id;
            await using (var db = await factory.CreateDbContextAsync())
            {
                var queued = await db.OutboxItems.AsNoTracking()
                    .Where(item => item.Kind == NotificationChannelCodes.Outbox).ToArrayAsync();
                var messages = queued.Select(item => JsonSerializer.Deserialize<NotificationMessage>(item.PayloadJson)!)
                    .ToArray();
                check("保密审批新待办与审批事件在同一 SQLite 提交中入队",
                    await db.ApprovalTasks.AnyAsync(item => item.Id == taskId) && queued.Length == 2 &&
                    messages.All(item => item.RecipientId == "user-n1" &&
                        item.Body.Contains("SP-202610-000001", StringComparison.Ordinal) &&
                        item.Body.Contains("采购申请", StringComparison.Ordinal) &&
                        item.TargetPath == "/m/approvals/instance"));
            }

            var result = await service.ExecuteAsync(new RuntimeCommandInput
            {
                Command = EngineScenarioChecks.Command(EngineCommandKinds.Approve, "notification-approve", taskId),
                Context = EngineScenarioChecks.Context(graph),
            });
            await using (var db = await factory.CreateDbContextAsync())
            {
                var messages = (await db.OutboxItems.AsNoTracking()
                    .Where(item => item.Kind == NotificationChannelCodes.Outbox).ToArrayAsync())
                    .Select(item => JsonSerializer.Deserialize<NotificationMessage>(item.PayloadJson)!).ToArray();
                check("审批结果只通知在职发起人并与完成状态一起持久提交",
                    result.State.Status == EngineInstanceStatuses.Approved &&
                    await db.Instances.AnyAsync(item => item.Id == "instance" &&
                        item.Status == EngineInstanceStatuses.Approved) &&
                    messages.Count(item => item.Category == NotificationCategories.Result &&
                        item.RecipientId == "requester") == 2 &&
                    !messages.Any(item => item.RecipientId == "outsider"));
            }

            var audience = await RuntimeNotificationAudience.ResolveAsync(organization,
                new RuntimeSnapshot
                {
                    Participants =
                    [
                        new InstanceParticipantEntity { PersonId = "historic" },
                    ],
                }, new InstanceEntity { InitiatorId = "requester" }, graph,
                new EngineResult
                {
                    State = new EngineState
                    {
                        Tasks =
                        [
                            new EngineTask { AssigneeId = "user-n1", Status = EngineTaskStatuses.Cancelled },
                            new EngineTask { AssigneeId = "inactive", Status = EngineTaskStatuses.Pending },
                        ],
                    },
                    Events =
                    [
                        new EngineEvent
                        {
                            Id = "mention", Kind = "commentAdded",
                            Audit = new OperationAudit { MentionedUserIds = ["historic", "outsider", "user-n1"] },
                        },
                    ],
                }, []);
            check("保密流程只授权历史实际参与人，排除旁观者、已取消指派与离职者",
                audience.AuthorizedIds.Contains("historic") &&
                !audience.AuthorizedIds.Contains("outsider") &&
                !audience.AuthorizedIds.Contains("user-n1") &&
                !audience.AuthorizedIds.Contains("inactive"));
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 建立最小在职人员资料。
    /// </summary>
    /// <param name="id">内部人员 ID。</param>
    /// <param name="email">邮箱。</param>
    /// <param name="active">是否在职。</param>
    /// <returns>人员资料。</returns>
    private static PersonEntity Person(string id, string email, bool active = true) => new()
    {
        Id = id,
        EmployeeNumber = id,
        Name = id,
        Email = email,
        IsActive = active,
    };

    /// <summary>
    /// 为每次操作建立独立的 SQLite 上下文。
    /// </summary>
    /// <param name="options">共享测试库选项。</param>
    private sealed class NotificationContextFactory(DbContextOptions<ApprovalDbContext> options)
        : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
