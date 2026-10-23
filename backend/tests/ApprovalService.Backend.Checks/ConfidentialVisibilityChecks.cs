using System.Text.Json;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Files;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 提交检查保密版本、抄送投影与附件权限。
/// </summary>
internal static class ConfidentialVisibilityChecks
{
    /// <summary>
    /// 运行参与人权限场景。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    /// <returns>异步检查。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "visibility.db")).Options;
            var factory = new VisibilityContextFactory(options);
            var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Cc);
            graph.IsConfidential = true;
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Flows.Add(new FlowEntity { Id = "flow", Code = "SECURE", Name = "保密审批" });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "version",
                    FlowId = "flow",
                    Version = 1,
                    DefinitionJson = JsonSerializer.Serialize(new FlowDefinition
                    {
                        Code = "SECURE",
                        Name = "保密审批",
                        IsConfidential = true
                    }, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
                });
                await db.SaveChangesAsync();
            }

            var runtime = new ApprovalRuntimeAppService(new ApprovalRuntimeRepository(factory));
            var files = new StoredFileRepository(factory);
            var context = EngineScenarioChecks.Context(graph);
            context.Operations.InitiatorId = "requester";
            var start = await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = Command(EngineCommandKinds.Start, "begin"),
                Context = context,
                StartInstance = new InstanceEntity
                {
                    Id = "secure",
                    SerialNo = "SECURE-1",
                    FlowVersionId = "version",
                    FlowCode = "SECURE",
                    BusinessKey = "1",
                    Title = "保密审批",
                    InitiatorId = "requester",
                    SubmittedById = "submitter",
                    ClientAppCode = "demo",
                },
            });
            check("保密版本发起人、代发人和当前任务人可看附件",
                await files.CanViewInstanceAsync("secure", "requester") &&
                await files.CanViewInstanceAsync("secure", "submitter") &&
                await files.CanViewInstanceAsync("secure", "user-n1"));
            check("抄送节点尚未送达时无权限且管理员没有自动权限",
                !await files.CanViewInstanceAsync("secure", "user-n2") &&
                !await files.CanViewInstanceAsync("secure", "administrator"));

            var taskId = start.State.Tasks.Single().Id;
            await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = Command(OperationKinds.CopyTo, "copy", taskId, "manual-copy"),
                Context = context,
            });
            check("人工抄送提交后收件人获得持久附件权限",
                await files.CanViewInstanceAsync("secure", "manual-copy"));
            var approved = await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = Command(EngineCommandKinds.Approve, "approve", taskId),
                Context = context,
            });
            check("普通抄送事件固定送达时的收件人并保留历史权限",
                approved.Events.Any(item => item.Kind == "ccDelivered" &&
                    item.ResolvedAssigneeIds.SequenceEqual(["user-n2"])) &&
                await files.CanViewInstanceAsync("secure", "user-n2") &&
                await files.CanViewInstanceAsync("secure", "user-n1"));
            check("嵌入会话必须同时属于审批单来源应用",
                await files.CanViewInstanceAsync("secure", "user-n2", "demo") &&
                !await files.CanViewInstanceAsync("secure", "user-n2", "other"));
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("普通与人工抄送和实际处理人随事件保存为三条参与人投影",
                    await db.InstanceParticipants.CountAsync(item => item.InstanceId == "secure") == 3 &&
                    await db.InstanceParticipants.CountAsync(item => item.InstanceId == "secure" &&
                        item.Kind == InstanceParticipantKinds.CopyRecipient) == 2 &&
                    await db.InstanceParticipants.CountAsync(item => item.InstanceId == "secure" &&
                        item.Kind == InstanceParticipantKinds.TaskActor) == 1);
                db.ApprovalTasks.Add(new ApprovalTaskEntity
                {
                    Id = "cancelled-task",
                    InstanceId = "secure",
                    NodeId = "n1",
                    AssigneeId = "cancelled",
                    Status = EngineTaskStatuses.Cancelled,
                });
                db.ApprovalTasks.Add(new ApprovalTaskEntity
                {
                    Id = "queued-task",
                    InstanceId = "secure",
                    NodeId = "n1",
                    AssigneeId = "queued",
                    Status = EngineTaskStatuses.Queued,
                });
                db.ApprovalTasks.Add(new ApprovalTaskEntity
                {
                    Id = "delegated-task",
                    InstanceId = "secure",
                    NodeId = "n1",
                    AssigneeId = "delegate",
                    DelegatedForId = "original-handler",
                    Status = EngineTaskStatuses.Pending,
                });
                await db.SaveChangesAsync();
            }
            check("保密流程已取消及排队任务和无关人员不因指派获得权限",
                !await files.CanViewInstanceAsync("secure", "cancelled") &&
                !await files.CanViewInstanceAsync("secure", "queued") &&
                !await files.CanViewInstanceAsync("secure", "outsider"));
            check("保密流程当前代理人与仍可办理的原审批人都能查看附件",
                await files.CanViewInstanceAsync("secure", "delegate") &&
                await files.CanViewInstanceAsync("secure", "original-handler"));
            var transferStart = await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = Command(EngineCommandKinds.Start, "transfer-start", instanceId: "transfer-instance"),
                Context = context,
                StartInstance = new InstanceEntity
                {
                    Id = "transfer-instance",
                    SerialNo = "SECURE-2",
                    FlowVersionId = "version",
                    FlowCode = "SECURE",
                    BusinessKey = "2",
                    Title = "转交退回",
                    InitiatorId = "requester",
                    ClientAppCode = "demo",
                },
            });
            var transferTaskId = transferStart.State.Tasks.Single().Id;
            await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = Command(OperationKinds.Transfer, "transfer", transferTaskId, "transferee",
                    "transfer-instance"),
                Context = context,
            });
            await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = Command(OperationKinds.Return, "return", transferTaskId,
                    instanceId: "transfer-instance", actorId: "transferee"),
                Context = context,
            });
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("转交与退回的真实处理人写入历史投影并保留附件权限",
                    await db.InstanceParticipants.CountAsync(item => item.InstanceId == "transfer-instance" &&
                        item.Kind == InstanceParticipantKinds.TaskActor) == 2 &&
                    await files.CanViewInstanceAsync("transfer-instance", "user-n1") &&
                    await files.CanViewInstanceAsync("transfer-instance", "transferee"));
            }
            check("旧 SQLite 库升级后保留实例任务且新增参与人表",
                await UpgradeOldSqliteAsync(directory));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 从新增参与人表前的完整历史迁移升级，不用模型直接建表代替迁移。
    /// </summary>
    /// <param name="parentDirectory">本次检查的临时根目录。</param>
    /// <returns>旧审批单保留且新表可查询时为真。</returns>
    private static async Task<bool> UpgradeOldSqliteAsync(string parentDirectory)
    {
        var path = Path.Combine(parentDirectory, "legacy.db");
        var options = new DbContextOptionsBuilder<SqliteApprovalDbContext>()
            .UseSqlite("Data Source=" + path).Options;
        await using (var old = new SqliteApprovalDbContext(options))
        {
            var previousMigration = old.Database.GetMigrations().Last(item =>
                !item.EndsWith("AddInstanceParticipants", StringComparison.Ordinal));
            await old.GetService<IMigrator>().MigrateAsync(previousMigration);
            old.Flows.Add(new FlowEntity { Id = "old-flow", Code = "OLD", Name = "旧流程" });
            old.FlowVersions.Add(new FlowVersionEntity
            {
                Id = "old-version",
                FlowId = "old-flow",
                Version = 1,
                DefinitionJson = "{}",
                GraphJson = "{}",
            });
            old.Instances.Add(new InstanceEntity
            {
                Id = "old-instance",
                SerialNo = "OLD-1",
                FlowId = "old-flow",
                FlowVersionId = "old-version",
                FlowCode = "OLD",
                BusinessKey = "1",
                Title = "旧审批",
                InitiatorId = "old-initiator",
                ClientAppCode = "demo",
            });
            old.ApprovalTasks.Add(new ApprovalTaskEntity
            {
                Id = "old-task",
                InstanceId = "old-instance",
                NodeId = "approval",
                AssigneeId = "old-handler",
                Status = EngineTaskStatuses.Approved,
            });
            await old.SaveChangesAsync();
        }

        await using (var upgraded = new SqliteApprovalDbContext(options))
        {
            await upgraded.Database.MigrateAsync();
            if (await upgraded.Instances.CountAsync(item => item.Id == "old-instance") != 1 ||
                await upgraded.ApprovalTasks.CountAsync(item => item.Id == "old-task") != 1 ||
                await upgraded.InstanceParticipants.CountAsync() != 0)
            {
                return false;
            }
        }

        var baseOptions = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseSqlite("Data Source=" + path).Options;
        var files = new StoredFileRepository(new VisibilityContextFactory(baseOptions));
        return await files.CanViewInstanceAsync("old-instance", "old-handler") &&
            !await files.CanViewInstanceAsync("old-instance", "outsider");
    }

    /// <summary>
    /// 构造固定身份的运行命令。
    /// </summary>
    /// <param name="kind">命令种类。</param>
    /// <param name="requestId">唯一请求 ID。</param>
    /// <param name="taskId">人工任务 ID。</param>
    /// <param name="targetId">抄送目标。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="actorId">实际操作人。</param>
    /// <returns>运行命令。</returns>
    private static EngineCommand Command(string kind, string requestId, string? taskId = null,
        string? targetId = null, string instanceId = "secure", string actorId = "user-n1") => new()
        {
            Kind = kind,
            InstanceId = instanceId,
            RequestId = requestId,
            TaskId = taskId,
            Operation = kind == EngineCommandKinds.Start ? null : new OperationInput
            {
                ActorId = actorId,
                TargetUserId = targetId,
            },
        };

    /// <summary>
    /// 为权限检查创建独立数据库上下文。
    /// </summary>
    /// <param name="options">SQLite 选项。</param>
    private sealed class VisibilityContextFactory(DbContextOptions<ApprovalDbContext> options)
        : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
