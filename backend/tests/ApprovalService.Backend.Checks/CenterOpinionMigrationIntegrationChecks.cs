using ApprovalService.Domain.Center;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 验证含接入应用管理员名单的旧 SQLite 库升级后仍保留旧数据。
/// </summary>
internal static class CenterOpinionMigrationIntegrationChecks
{
    /// <summary>
    /// 先迁移到管理员名单版本，再升级到个人常用意见版本。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>检查完成。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var path = Path.Combine(directory, "center-opinions-upgrade.db");
            var options = new DbContextOptionsBuilder<SqliteApprovalDbContext>()
                .UseSqlite("Data Source=" + path).Options;
            await using (var old = new SqliteApprovalDbContext(options))
            {
                await old.GetService<IMigrator>().MigrateAsync("20261025060636_ClientAppAdministrators");
                // 旧库尚无现行模型的新列，必须按当时表结构写入升级夹具。
                await old.Database.ExecuteSqlRawAsync("""
                    INSERT INTO ClientApps
                    (Code, Name, ApiKeyHash, SigningSecretCiphertext, AllowedFlowCodesJson,
                     AllowedEmbedOriginsJson, AdministratorPersonIdsJson, CanViewAllTasks, IsEnabled, AuthGeneration)
                    VALUES ('legacy-app', '原接入应用', @hash, '旧密文', '[]', '[]', '["administrator"]', 0, 1, 1)
                    """, new Microsoft.Data.Sqlite.SqliteParameter("@hash", new string('a', 64)));
            }

            await using var upgraded = new SqliteApprovalDbContext(options);
            await upgraded.Database.MigrateAsync();
            var app = await upgraded.ClientApps.SingleAsync(item => item.Code == "legacy-app");
            check("接入应用管理员名单旧库升级后保留原应用和管理员名单",
                app.Name == "原接入应用" && app.AdministratorPersonIdsJson == "[\"administrator\"]");
            upgraded.CenterOpinions.Add(new CenterOpinionEntity
            {
                Id = "opinion-upgrade",
                PersonId = "administrator",
                Content = "已核对",
                CreatedAtUtc = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc),
            });
            await upgraded.SaveChangesAsync();
            check("管理员名单旧库升级后可写入个人常用意见",
                await upgraded.CenterOpinions.CountAsync(item => item.PersonId == "administrator") == 1);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }
}
