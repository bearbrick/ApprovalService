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
                    },
                    new OperationEventEntity
                    {
                        Id = "activated-a2",
                        InstanceId = "a",
                        Sequence = 3,
                        Kind = "taskActivated",
                        PayloadJson = JsonSerializer.Serialize(new EngineEvent
                        {
                            Id = "activated-a2",
                            Kind = "taskActivated",
                            TaskId = "task-a2",
                            NodeId = "finance",
                            OccurredAt = nextDay.AddMinutes(2),
                        }, ApprovalJson.Options),
                        OccurredAt = nextDay.AddMinutes(2),
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
            check("依次审批的真实任务激活时间单独读取",
                selected.TaskActivatedEvents.Count == 1 && selected.TaskActivatedEvents[0].TaskId == "task-a2" &&
                selected.TaskActivatedEvents[0].OccurredAt == nextDay.AddMinutes(2));
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

            await using (var db = await factory.CreateDbContextAsync())
            {
                // 三张旧审批单分别验证当前待办、到期任务及观察时刻之后才到期的排除边界。
                db.Instances.AddRange(
                    new InstanceEntity
                    {
                        Id = "d",
                        SerialNo = "AP-R-4",
                        FlowId = "purchase",
                        FlowVersionId = "purchase-v1",
                        FlowCode = "PURCHASE",
                        BusinessKey = "PO-4",
                        Title = "旧单当前待办",
                        InitiatorId = "starter",
                        InitiatorDepartmentId = "D1",
                        ClientAppCode = "ERP",
                        Status = EngineInstanceStatuses.Running,
                        StartedAt = start.AddHours(-1),
                    },
                    new InstanceEntity
                    {
                        Id = "e",
                        SerialNo = "AP-R-5",
                        FlowId = "expense",
                        FlowVersionId = "expense-v1",
                        FlowCode = "EXPENSE",
                        BusinessKey = "EX-5",
                        Title = "旧单仅因到期纳入",
                        InitiatorId = "starter",
                        InitiatorDepartmentId = "D2",
                        ClientAppCode = "FINANCE",
                        Status = EngineInstanceStatuses.Approved,
                        StartedAt = start.AddHours(-3),
                        EndedAt = start.AddHours(-1),
                    },
                    new InstanceEntity
                    {
                        Id = "f",
                        SerialNo = "AP-R-6",
                        FlowId = "expense",
                        FlowVersionId = "expense-v1",
                        FlowCode = "EXPENSE",
                        BusinessKey = "EX-6",
                        Title = "观察时尚未到期",
                        InitiatorId = "starter",
                        InitiatorDepartmentId = "D2",
                        ClientAppCode = "FINANCE",
                        Status = EngineInstanceStatuses.Approved,
                        StartedAt = start.AddHours(-4),
                        EndedAt = start.AddHours(-2),
                    });
                db.ApprovalTasks.AddRange(
                    new ApprovalTaskEntity
                    {
                        Id = "task-d1",
                        InstanceId = "d",
                        NodeId = "manager",
                        AssigneeId = "reviewer",
                        Status = EngineTaskStatuses.Pending,
                    },
                    new ApprovalTaskEntity
                    {
                        Id = "task-e1",
                        InstanceId = "e",
                        NodeId = "finance",
                        AssigneeId = "reviewer",
                        Status = EngineTaskStatuses.Approved,
                        DueAt = nextDay.AddHours(2),
                        CompletedAt = start.AddHours(-1),
                    },
                    new ApprovalTaskEntity
                    {
                        Id = "task-f1",
                        InstanceId = "f",
                        NodeId = "finance",
                        AssigneeId = "reviewer",
                        Status = EngineTaskStatuses.Approved,
                        DueAt = nextDay.AddHours(18),
                        CompletedAt = start.AddHours(-2),
                    });
                await db.SaveChangesAsync();
            }

            var metricsQuery = new ReportingMetricsQuery
            {
                FromUtc = nextDay,
                UntilUtc = nextDay.AddDays(1),
                AsOfUtc = nextDay.AddHours(12),
            };
            var metricFacts = await repository.ReadForMetricsAsync(metricsQuery);
            check("跨期报表事实取发起、结束、已到期和当前待办的并集",
                metricFacts.Instances.Select(item => item.Id).Order().SequenceEqual(["a", "b", "d", "e"]));
            check("观察时未到期的旧单和筛选期前完成且未到期的旧单不混入",
                !metricFacts.Instances.Any(item => item.Id == "f" || item.Id == "c") &&
                metricFacts.Tasks.Any(item => item.Id == "task-e1") &&
                metricFacts.Tasks.Any(item => item.Id == "task-d1"));
            var metricResult = ReportingMetricsCalculator.Calculate(metricFacts, metricsQuery);
            check("期前发起但期内结束的审批单计入完成量且期内新单计入发起量",
                metricResult.Flows.Single(item => item.FlowCode == "PURCHASE").StartedCount == 1 &&
                metricResult.Flows.Single(item => item.FlowCode == "PURCHASE").CompletedCount == 1);
            check("到期分母纳入期前发起的审批任务，当前积压也跨发起时间读取",
                metricResult.Nodes.Sum(item => item.DueCount) == 2 &&
                metricResult.People.Sum(item => item.PendingCount) == 3);

            var scopedFacts = await repository.ReadForMetricsAsync(new ReportingMetricsQuery
            {
                FromUtc = nextDay,
                UntilUtc = nextDay.AddDays(1),
                AsOfUtc = nextDay.AddHours(12),
                FlowCode = "PURCHASE",
                ClientAppCode = "ERP",
                InitiatorDepartmentId = "D1",
            });
            check("跨期各入口统一应用流程、来源应用和发起部门快照筛选",
                scopedFacts.Instances.Select(item => item.Id).Order().SequenceEqual(["a", "d"]));
            var safeProjection = JsonSerializer.Serialize(metricFacts);
            check("跨期事实仍不读取审批标题与业务数据",
                !safeProjection.Contains("旧单", StringComparison.Ordinal) &&
                !safeProjection.Contains("业务数据", StringComparison.Ordinal));
            var invalidMetricsRange = false;
            try
            {
                await repository.ReadForMetricsAsync(new ReportingMetricsQuery
                {
                    FromUtc = nextDay,
                    UntilUtc = nextDay,
                    AsOfUtc = nextDay,
                });
            }
            catch (ArgumentException)
            {
                invalidMetricsRange = true;
            }

            check("跨期读取拒绝空时间范围", invalidMetricsRange);
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
