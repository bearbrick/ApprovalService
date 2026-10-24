using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 验证未读游标已落库的旧 SQLite 数据可升级到整合后的流程管理模型。
/// </summary>
internal static class FlowManagementMigrationIntegrationChecks
{
    /// <summary>
    /// 从整合前的最后一项迁移升级，并核对旧流程和新表。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>检查结束。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var path = Path.Combine(directory, "legacy-flow.db");
            var options = new DbContextOptionsBuilder<SqliteApprovalDbContext>()
                .UseSqlite("Data Source=" + path).Options;
            await using (var old = new SqliteApprovalDbContext(options))
            {
                // 旧模型没有流程管理新增列，因此用旧表的实际列写入存量流程。
                await old.GetService<IMigrator>().MigrateAsync("20261024140303_AddCenterReadCursors");
                await old.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Flows (Id, Code, Name, AdministratorId, CreatedAt) " +
                    "VALUES ('legacy-flow', 'LEGACY_FLOW', '旧流程', 'legacy-admin', '2026-10-01 00:00:00')");
            }

            await using var upgraded = new SqliteApprovalDbContext(options);
            await upgraded.Database.MigrateAsync();
            var flow = await upgraded.Flows.SingleAsync(item => item.Id == "legacy-flow");
            check("未读游标旧库升级后保留流程与管理员原值，新增管理字段取默认值",
                flow.Code == "LEGACY_FLOW" && flow.AdministratorId == "legacy-admin" &&
                flow.OwnerDepartmentId is null && flow.IsEnabled);
            check("旧库升级后流程管理新表和既有未读游标表均可读取",
                await upgraded.FlowDrafts.CountAsync() == 0 &&
                await upgraded.FlowManagementAudits.CountAsync() == 0 &&
                await upgraded.FlowAdministratorIdentityIssues.CountAsync() == 0 &&
                await upgraded.CenterReadCursors.CountAsync() == 0);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }
}
