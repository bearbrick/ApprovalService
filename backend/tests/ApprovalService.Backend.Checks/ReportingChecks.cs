using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Domain.Reporting;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using ApprovalService.Infrastructure.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 检查统计事实读取、筛选及不可变任务创建时间。
/// </summary>
internal static class ReportingChecks
{
    /// <summary>
    /// 建立独立数据库并验证报表事实边界。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-reporting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "reporting.db"))
                .Options;
            var factory = new ReportingContextFactory(options);
            var start = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
            var nextDay = start.AddDays(1);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Flows.AddRange(
                    new FlowEntity { Id = "purchase", Code = "PURCHASE", Name = "采购审批" },
                    new FlowEntity { Id = "expense", Code = "EXPENSE", Name = "费用审批" });
                db.FlowVersions.AddRange(
                    new FlowVersionEntity
                    {
                        Id = "purchase-v1",
                        FlowId = "purchase",
                        Version = 1,
                        DefinitionJson = "{}",
                        GraphJson = "{}"
                    },
                    new FlowVersionEntity
                    {
                        Id = "expense-v1",
                        FlowId = "expense",
                        Version = 1,
                        DefinitionJson = "{}",
                        GraphJson = "{}"
                    });
                db.Instances.AddRange(
                    new InstanceEntity
                    {
                        Id = "a",
                        SerialNo = "AP-R-1",
                        FlowId = "purchase",
                        FlowVersionId = "purchase-v1",
                        FlowCode = "PURCHASE",
                        BusinessKey = "PO-1",
                        Title = "不能出现在报表的保密标题",
                        InitiatorId = "starter",
                        InitiatorDepartmentId = "D1",
                        ClientAppCode = "ERP",
                        ApprovalDataJson = "{\"secret\":\"不能出现在报表的业务数据\"}",
                        Status = EngineInstanceStatuses.Approved,
                        StartedAt = start,
                        EndedAt = nextDay.AddMinutes(5),
                    },
                    new InstanceEntity
                    {
                        Id = "b",
                        SerialNo = "AP-R-2",
                        FlowId = "purchase",
                        FlowVersionId = "purchase-v1",
                        FlowCode = "PURCHASE",
                        BusinessKey = "PO-2",
                        Title = "跨日边界",
                        InitiatorId = "starter",
                        InitiatorDepartmentId = "D2",
                        ClientAppCode = "ERP",
                        Status = EngineInstanceStatuses.Running,
                        StartedAt = nextDay,
                    },
                    new InstanceEntity
                    {
                        Id = "c",
                        SerialNo = "AP-R-3",
                        FlowId = "expense",
                        FlowVersionId = "expense-v1",
                        FlowCode = "EXPENSE",
                        BusinessKey = "EX-1",
                        Title = "另一来源",
                        InitiatorId = "starter",
                        InitiatorDepartmentId = "D1",
                        ClientAppCode = "FINANCE",
                        Status = EngineInstanceStatuses.Rejected,
                        StartedAt = start.AddHours(1),
                        EndedAt = start.AddHours(2),
                    });
                db.ApprovalTasks.AddRange(
                    new ApprovalTaskEntity
                    {
                        Id = "task-a1",
                        InstanceId = "a",
                        NodeId = "manager",
                        AssigneeId = "delegate",
                        DelegatedForId = "manager",
                        Status = EngineTaskStatuses.Approved,
                        DueAt = nextDay,
                        CompletedAt = nextDay.AddMinutes(5),
                    },
                    new ApprovalTaskEntity
                    {
                        Id = "task-a2",
                        InstanceId = "a",
                        NodeId = "finance",
                        AssigneeId = "reviewer",
                        Status = EngineTaskStatuses.Pending,
                    },
                    new ApprovalTaskEntity
                    {
                        Id = "task-b1",
                        InstanceId = "b",
                        NodeId = "manager",
                        AssigneeId = "other",
                        Status = EngineTaskStatuses.Pending,
                    });
                db.OperationEvents.AddRange(
                    new OperationEventEntity
                    {
                        Id = "created-a1",
                        InstanceId = "a",
                        Sequence = 1,
                        Kind = "taskCreated",
                        PayloadJson = JsonSerializer.Serialize(new EngineEvent
                        {
                            Id = "created-a1",
                            Kind = "taskCreated",
                            TaskId = "task-a1",
                            NodeId = "manager",
                            OccurredAt = start.AddMinutes(3),
                        }, ApprovalJson.Options),
                        OccurredAt = start.AddMinutes(3),
                    },
                    new OperationEventEntity
                    {
                        Id = "other-a",
                        InstanceId = "a",
                        Sequence = 2,
                        Kind = "taskCompleted",
                        PayloadJson = "{}",
                        OccurredAt = nextDay.AddMinutes(5),
                    },
                    new OperationEventEntity
                    {
                        Id = "created-b1",
                        InstanceId = "b",
                        Sequence = 1,
                        Kind = "taskCreated",
                        PayloadJson = JsonSerializer.Serialize(new EngineEvent
                        {
                            Id = "created-b1",
                            Kind = "taskCreated",
                            TaskId = "task-b1",
                            NodeId = "manager",
                            OccurredAt = nextDay.AddMinutes(1),
                        }, ApprovalJson.Options),
                        OccurredAt = nextDay.AddMinutes(1),
                    });
                await db.SaveChangesAsync();
            }

            var repository = new ReportingFactsRepository(factory);
            var selected = await repository.ReadAsync(new ReportingFactsQuery
            {
                InstanceStartedFromUtc = start,
                InstanceStartedUntilUtc = nextDay,
                FlowCode = "PURCHASE",
                ClientAppCode = "ERP",
                InitiatorDepartmentId = "D1",
            });
            check("报表按发起时间半开区间及流程、来源、发起部门快照筛选",
                selected.Instances.Count == 1 && selected.Instances[0].Id == "a" &&
                selected.Instances[0].StartedAt == start && selected.Instances[0].EndedAt == nextDay.AddMinutes(5));
            check("任务事实保留代理人、原审批人、节点、到期与处理时间",
                selected.Tasks.Count == 2 && selected.Tasks[0].Id == "task-a1" &&
                selected.Tasks[0].AssigneeId == "delegate" && selected.Tasks[0].DelegatedForId == "manager" &&
                selected.Tasks[0].NodeId == "manager" && selected.Tasks[0].DueAt == nextDay &&
                selected.Tasks[0].CompletedAt == nextDay.AddMinutes(5));
            check("任务创建时间只来自真实引擎事件，缺失事件不伪造",
                selected.TaskCreatedEvents.Count == 1 && selected.TaskCreatedEvents[0].TaskId == "task-a1" &&
                selected.TaskCreatedEvents[0].OccurredAt == start.AddMinutes(3) &&
                !selected.TaskCreatedEvents.Any(item => item.TaskId == "task-a2"));
            var serialized = JsonSerializer.Serialize(selected);
            check("统计事实不携带审批标题或业务数据", !serialized.Contains("保密标题", StringComparison.Ordinal) &&
                !serialized.Contains("业务数据", StringComparison.Ordinal));

            var boundary = await repository.ReadAsync(new ReportingFactsQuery
            {
                InstanceStartedFromUtc = nextDay,
                InstanceStartedUntilUtc = nextDay.AddDays(1),
            });
            check("跨日上界不重复统计次日审批单", boundary.Instances.Count == 1 &&
                boundary.Instances[0].Id == "b" && boundary.Tasks.Count == 1 &&
                boundary.TaskCreatedEvents.Count == 1);
            var finance = await repository.ReadAsync(new ReportingFactsQuery
            {
                InstanceStartedFromUtc = start,
                InstanceStartedUntilUtc = nextDay,
                ClientAppCode = "FINANCE",
            });
            check("来源应用筛选不会混入其他流程", finance.Instances.Count == 1 &&
                finance.Instances[0].Id == "c" && finance.Tasks.Count == 0);

            var invalidRangeRejected = false;
            try
            {
                await repository.ReadAsync(new ReportingFactsQuery
                {
                    InstanceStartedFromUtc = start,
                    InstanceStartedUntilUtc = start,
                });
            }
            catch (ArgumentException)
            {
                invalidRangeRejected = true;
            }

            check("统计事实读取拒绝空时间区间", invalidRangeRejected);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 为每次读取提供独立上下文。
    /// </summary>
    /// <param name="options">SQLite 临时数据库配置。</param>
    private sealed class ReportingContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
