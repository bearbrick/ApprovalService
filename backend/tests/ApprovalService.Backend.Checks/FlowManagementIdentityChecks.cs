using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Administration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 检查流程直接部门权限和存量管理员标识的保守迁移。
/// </summary>
internal static class FlowManagementIdentityChecks
{
    /// <summary>
    /// 执行权限与真实 SQLite 数据迁移场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        CheckScope(check);
        var directory = Path.Combine(Path.GetTempPath(), "approval-flow-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<SqliteApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "identity.db")).Options;
            var factory = new ContextFactory(options);
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.People.AddRange(
                    new PersonEntity { Id = "person-1", EmployeeNumber = "E1", Name = "甲" },
                    new PersonEntity { Id = "E1", EmployeeNumber = "E2", Name = "乙" },
                    new PersonEntity { Id = "person-3", EmployeeNumber = "E3", Name = "丙" },
                    new PersonEntity { Id = "person-4", EmployeeNumber = "E4", Name = "丁" });
                db.Flows.AddRange(
                    Flow("unique", "E3"), Flow("ambiguous", "E1"), Flow("missing", "unknown"),
                    Flow("internal", "person-4"), Flow("empty", null));
                await db.SaveChangesAsync();
            }

            var reconciler = new FlowAdministratorIdentityReconciler(factory, TimeProvider.System);
            var preview = await reconciler.PreviewAsync();
            check("本机对账预览列出唯一修正和零或多匹配歧义",
                preview.PlannedUpdates.Count == 1 && preview.Unresolved.Count == 2 &&
                preview.Unresolved.Any(item => item.FlowCode == "ambiguous" && item.MatchCount == 2));
            await reconciler.ReconcileAsync(preview.Fingerprint);
            await using (var db = factory.CreateDbContext())
            {
                check("旧工号仅唯一匹配时改成内部人员 ID",
                    await db.Flows.AnyAsync(item => item.Id == "unique" && item.AdministratorId == "person-3") &&
                    await db.Flows.AnyAsync(item => item.Id == "internal" && item.AdministratorId == "person-4"));
                check("双重匹配与无匹配明确留待人工处理",
                    await db.Flows.AnyAsync(item => item.Id == "ambiguous" && item.AdministratorId == "E1") &&
                    await db.FlowAdministratorIdentityIssues.AnyAsync(item => item.FlowId == "ambiguous" &&
                        item.LegacyValue == "E1" && item.MatchCount == 2) &&
                    await db.FlowAdministratorIdentityIssues.AnyAsync(item => item.FlowId == "missing" &&
                        item.MatchCount == 0) &&
                    !await db.FlowAdministratorIdentityIssues.AnyAsync(item => item.FlowId == "empty"));
                var issue = await db.FlowAdministratorIdentityIssues.SingleAsync(item => item.FlowId == "ambiguous");
                var flow = await db.Flows.SingleAsync(item => item.Id == "ambiguous");
                flow.AdministratorId = "person-1";
                await db.SaveChangesAsync();
            }

            await reconciler.ReconcileAsync((await reconciler.PreviewAsync()).Fingerprint);
            await using (var db = factory.CreateDbContext())
            {
                check("人工修正后的歧义事项随下一次核对清除",
                    !await db.FlowAdministratorIdentityIssues.AnyAsync(item => item.FlowId == "ambiguous") &&
                    await db.FlowAdministratorIdentityIssues.CountAsync() == 1);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 检查组织管理员与部门流程管理员的直接归属边界。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    private static void CheckScope(Action<string, bool> check)
    {
        var flow = new FlowEntity { Id = "flow", OwnerDepartmentId = "dept-child", AdministratorId = "actor" };
        check("在职本地组织管理员可管理全部流程",
            FlowManagementAccessRules.CanManage(true, true, true, false, [], flow));
        check("部门流程管理员仅管理直接所属部门流程",
            FlowManagementAccessRules.CanManage(true, true, false, true, ["dept-child"], flow) &&
            !FlowManagementAccessRules.CanManage(true, true, false, true, ["dept-parent"], flow));
        check("流程管理员身份不能绕过角色、部门和会话边界",
            !FlowManagementAccessRules.CanManage(true, true, false, false, ["dept-child"], flow) &&
            !FlowManagementAccessRules.CanManage(false, true, true, true, ["dept-child"], flow) &&
            !FlowManagementAccessRules.CanManage(true, false, true, true, ["dept-child"], flow) &&
            !FlowManagementAccessRules.CanManage(true, true, false, true, ["dept-child"],
                new FlowEntity { Id = "unowned" }));
    }

    /// <summary>
    /// 创建用于迁移场景的流程元数据。
    /// </summary>
    /// <param name="id">流程 ID。</param>
    /// <param name="administratorId">存量管理员标识。</param>
    /// <returns>流程实体。</returns>
    private static FlowEntity Flow(string id, string? administratorId) => new()
    {
        Id = id,
        Code = id,
        Name = id,
        AdministratorId = administratorId,
    };

    /// <summary>
    /// 检查用上下文工厂。
    /// </summary>
    /// <param name="options">SQLite 上下文配置。</param>
    private sealed class ContextFactory(DbContextOptions<SqliteApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new SqliteApprovalDbContext(options);
    }
}
