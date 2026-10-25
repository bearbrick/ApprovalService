using ApprovalService.Domain.Center;
using ApprovalService.Domain.Integration;
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
                old.ClientApps.Add(new ClientAppEntity
                {
                    Code = "legacy-app",
                    Name = "原接入应用",
                    ApiKeyHash = new string('a', 64),
                    SigningSecretCiphertext = "旧密文",
                    AdministratorPersonIdsJson = "[\"administrator\"]",
                });
                await old.SaveChangesAsync();
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
