using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Reporting;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Reporting;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Organization;
using ApprovalService.Infrastructure.Persistence;
using ApprovalService.Infrastructure.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用真实 SQLite 校验报表角色和锁定版本保密边界。
/// </summary>
internal static class ReportingQueryChecks
{
    /// <summary>
    /// 建立公开与保密流程样本，检查内部查询的返回范围。
    /// </summary>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步检查任务。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-report-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "reporting.db")).Options;
            var factory = new ReportingQueryContextFactory(options);
            var start = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.People.AddRange(
                    new PersonEntity { Id = "admin", EmployeeNumber = "A1", Name = "管理员" },
                    new PersonEntity { Id = "reader", EmployeeNumber = "R1", Name = "普通人员" },
                    new PersonEntity { Id = "head", EmployeeNumber = "H1", Name = "部门负责人" },
                    new PersonEntity { Id = "inactive", EmployeeNumber = "I1", Name = "停用管理员", IsActive = false });
                db.Departments.AddRange(
                    new DepartmentEntity { Id = "D1", Code = "D1", Name = "公开部门", HeadPersonId = "head" },
                    new DepartmentEntity { Id = "D2", Code = "D2", Name = "子部门", ParentId = "D1" },
                    new DepartmentEntity { Id = "D3", Code = "D3", Name = "另一直属部门", HeadPersonId = "head" });
                db.OrganizationRoles.Add(new OrganizationRoleEntity { Code = "organization-admin", Name = "组织管理员" });
                db.PersonRoles.AddRange(
                    new PersonRoleEntity { PersonId = "admin", RoleCode = "organization-admin" },
                    new PersonRoleEntity { PersonId = "inactive", RoleCode = "organization-admin" });
                db.Flows.AddRange(
                    new FlowEntity { Id = "public", Code = "PUBLIC", Name = "公开流程" },
                    new FlowEntity { Id = "secret", Code = "SECRET", Name = "保密流程" },
                    new FlowEntity { Id = "broken", Code = "BROKEN", Name = "损坏样本" });
                db.FlowVersions.AddRange(
                    new FlowVersionEntity
                    {
                        Id = "public-v1",
                        FlowId = "public",
                        Version = 1,
                        DefinitionJson = "{}",
                        GraphJson = "{}",
                    },
                    new FlowVersionEntity
                    {
                        Id = "secret-v1",
                        FlowId = "secret",
                        Version = 1,
                        DefinitionJson = "{}",
                        GraphJson = JsonSerializer.Serialize(new FlowExecutionGraph { IsConfidential = true }, ApprovalJson.Options),
                    },
                    new FlowVersionEntity
                    {
                        Id = "broken-v1",
                        FlowId = "broken",
                        Version = 1,
                        DefinitionJson = "{}",
                        GraphJson = "{invalid",
                    });
                db.Instances.AddRange(
                    new InstanceEntity
                    {
                        Id = "public-a",
                        SerialNo = "AP-R-Q1",
                        FlowId = "public",
                        FlowVersionId = "public-v1",
                        FlowCode = "PUBLIC",
                        BusinessKey = "B1",
                        Title = "公开标题",
                        InitiatorId = "reader",
                        InitiatorDepartmentId = "D1",
                        ClientAppCode = "ERP",
                        Status = EngineInstanceStatuses.Approved,
                        StartedAt = start.AddHours(1),
                        EndedAt = start.AddHours(2),
                    },
                    new InstanceEntity
                    {
                        Id = "secret-b",
                        SerialNo = "AP-R-Q2",
                        FlowId = "secret",
                        FlowVersionId = "secret-v1",
                        FlowCode = "SECRET",
                        BusinessKey = "B2",
                        Title = "绝密标题",
                        InitiatorId = "reader",
                        InitiatorDepartmentId = "D2",
                        ClientAppCode = "ERP",
                        Status = EngineInstanceStatuses.Rejected,
                        StartedAt = start.AddHours(1),
                        EndedAt = start.AddHours(3),
                    },
                    new InstanceEntity
                    {
                        Id = "secret-old",
                        SerialNo = "AP-R-Q3",
                        FlowId = "secret",
                        FlowVersionId = "secret-v1",
                        FlowCode = "SECRET",
                        BusinessKey = "B3",
                        Title = "跨期保密标题",
                        InitiatorId = "reader",
                        InitiatorDepartmentId = "D2",
                        ClientAppCode = "ERP",
                        Status = EngineInstanceStatuses.Approved,
                        StartedAt = start.AddDays(-1),
                        EndedAt = start.AddHours(4),
                    },
                    new InstanceEntity
                    {
                        Id = "broken-sample",
                        SerialNo = "AP-R-Q4",
                        FlowId = "broken",
                        FlowVersionId = "broken-v1",
                        FlowCode = "BROKEN",
                        BusinessKey = "B4",
                        Title = "损坏样本",
                        InitiatorId = "reader",
                        InitiatorDepartmentId = "D3",
                        ClientAppCode = "ERP",
                        Status = EngineInstanceStatuses.Approved,
                        StartedAt = start.AddDays(2),
                        EndedAt = start.AddDays(2).AddHours(1),
                    });
                db.ApprovalTasks.AddRange(
                    new ApprovalTaskEntity
                    {
                        Id = "public-task",
                        InstanceId = "public-a",
                        NodeId = "review",
                        AssigneeId = "safe-person",
                        Status = EngineTaskStatuses.Approved,
                        DueAt = start.AddHours(2),
                        CompletedAt = start.AddHours(2),
                    },
                    new ApprovalTaskEntity
                    {
                        Id = "secret-task",
                        InstanceId = "secret-b",
                        NodeId = "secret-review",
                        AssigneeId = "secret-person",
                        Status = EngineTaskStatuses.Pending,
                        DueAt = start.AddHours(5),
                    });
                await db.SaveChangesAsync();
            }

            var repository = new ReportingFactsRepository(factory);
            var organization = new OrganizationRepository(factory);
            var service = new ReportingQueryAppService(organization, repository,
                new FixedReportingTimeProvider(start.AddHours(12)));
            var input = new ReportingQueryInput { FromUtc = start, UntilUtc = start.AddDays(1) };
            var facts = await repository.ReadForMetricsAsync(new ReportingMetricsQuery
            {
                FromUtc = start,
                UntilUtc = start.AddDays(1),
                AsOfUtc = start.AddHours(12),
            });
            check("报表事实保密性来自发起时锁定版本，旧图缺省非保密",
                facts.Instances.Single(item => item.Id == "public-a").IsConfidential == false &&
                facts.Instances.Single(item => item.Id == "secret-b").IsConfidential &&
                facts.Instances.Single(item => item.Id == "secret-old").IsConfidential);
            var result = await service.GetAsync(input, "admin", IdentitySessionKinds.Local);
            check("组织管理员看到公开流程指标与保密匿名发起完成数量",
                result.VisibleMetrics.Flows.Single().FlowCode == "PUBLIC" &&
                result.VisibleMetrics.Flows.Single().StartedCount == 1 &&
                result.ConfidentialStartedCount == 1 && result.ConfidentialCompletedCount == 2);
            var responseJson = JsonSerializer.Serialize(result);
            check("保密流程编码、节点及处理人均不出现在返回结果",
                !responseJson.Contains("SECRET", StringComparison.Ordinal) &&
                !responseJson.Contains("secret-review", StringComparison.Ordinal) &&
                !responseJson.Contains("secret-person", StringComparison.Ordinal) &&
                result.VisibleMetrics.People.Single().AssigneeId == "safe-person");
            var headResult = await service.GetAsync(input, "head", IdentitySessionKinds.Local);
            check("部门负责人只能看到直接负责部门，子部门保密数据不随父部门授权",
                headResult.VisibleMetrics.Flows.Single().FlowCode == "PUBLIC" &&
                headResult.ConfidentialStartedCount == 0 && headResult.ConfidentialCompletedCount == 0);
            check("部门负责人显式请求下级或非直属部门被拒绝",
                await IsRejectedAsync(() => service.GetAsync(new ReportingQueryInput
                {
                    FromUtc = start,
                    UntilUtc = start.AddDays(1),
                    InitiatorDepartmentId = "D2",
                }, "head", IdentitySessionKinds.Local), ErrorCodes.Forbidden));
            check("嵌入会话、普通人员与停用管理员均不能调用内部报表",
                await IsRejectedAsync(() => service.GetAsync(input, "admin", IdentitySessionKinds.Embed), ErrorCodes.Forbidden) &&
                await IsRejectedAsync(() => service.GetAsync(input, "reader", IdentitySessionKinds.Local), ErrorCodes.Forbidden) &&
                await IsRejectedAsync(() => service.GetAsync(input, "inactive", IdentitySessionKinds.Local), ErrorCodes.Forbidden));
            check("缺少认证人员与错误时间范围返回可修正错误",
                await IsRejectedAsync(() => service.GetAsync(input, string.Empty, IdentitySessionKinds.Local), ErrorCodes.Unauthorized) &&
                await IsRejectedAsync(() => service.GetAsync(new ReportingQueryInput
                {
                    FromUtc = start,
                    UntilUtc = start,
                }, "admin", IdentitySessionKinds.Local), ErrorCodes.ValidationFailed));

            var rejectsBrokenGraph = false;
            try
            {
                await service.GetAsync(new ReportingQueryInput
                {
                    FromUtc = start.AddDays(2),
                    UntilUtc = start.AddDays(3),
                }, "admin", IdentitySessionKinds.Local);
            }
            catch (InvalidDataException)
            {
                rejectsBrokenGraph = true;
            }

            check("损坏的锁定执行图不能默认公开保密流程", rejectsBrokenGraph);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 捕获预期的使用者可修正错误码。
    /// </summary>
    /// <param name="action">待执行查询。</param>
    /// <param name="code">预期错误码。</param>
    /// <returns>错误码匹配时为真。</returns>
    private static async Task<bool> IsRejectedAsync(Func<Task<ReportingQueryDto>> action, string code)
    {
        try
        {
            await action();
            return false;
        }
        catch (UserFriendlyException exception)
        {
            return exception.Code == code;
        }
    }

    /// <summary>
    /// 为检查提供固定的 UTC 观察时间。
    /// </summary>
    /// <param name="utcNow">固定时间。</param>
    private sealed class FixedReportingTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    /// <summary>
    /// 为每次读取提供独立 SQLite 上下文。
    /// </summary>
    /// <param name="options">临时数据库配置。</param>
    private sealed class ReportingQueryContextFactory(DbContextOptions<ApprovalDbContext> options)
        : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
