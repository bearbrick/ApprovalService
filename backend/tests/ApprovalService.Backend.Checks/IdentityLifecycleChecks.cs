using System.Security.Cryptography;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Identity;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 在真实 SQLite 组织事务中检查身份令牌代际不会随状态恢复而复活。
/// </summary>
internal static class IdentityLifecycleChecks
{
    /// <summary>
    /// 检查可信导入、可信推送和换人绑定的令牌生命周期。
    /// </summary>
    /// <param name="check">场景结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-identity-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "identity-lifecycle.db")).Options;
            var factory = new CheckContextFactory(options);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.ClientApps.Add(new ClientAppEntity
                {
                    Code = "demo",
                    Name = "演示业务系统",
                    ApiKeyHash = new string('a', 64),
                    SigningSecretCiphertext = "test-ciphertext",
                });
                db.People.AddRange(
                    new PersonEntity { Id = "p1", EmployeeNumber = "E1", Name = "甲" },
                    new PersonEntity { Id = "p2", EmployeeNumber = "E2", Name = "乙", TrustedSource = "demo" },
                    new PersonEntity { Id = "p3", EmployeeNumber = "E3", Name = "未开通账号" });
                db.LocalAccounts.AddRange(
                    new LocalAccountEntity { PersonId = "p1", PasswordHash = "hash:password-1", PasswordSalt = "salt" },
                    new LocalAccountEntity { PersonId = "p2", PasswordHash = "hash:password-2", PasswordSalt = "salt" });
                db.ExternalPersonBindings.Add(new ExternalPersonBindingEntity
                {
                    ClientAppCode = "demo",
                    ExternalUserId = "external",
                    PersonId = "p1",
                    Method = "test",
                });
                await db.SaveChangesAsync();
            }

            var identities = new IdentityAppService(new IdentityRepository(factory), new CheckPasswordService(),
                new JwtIdentityTokenService(Options.Create(new IdentityTokenOptions
                {
                    SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                }), Options.Create(new DataOptions { Directory = directory })), TimeProvider.System);
            var organization = new OrganizationRepository(factory);
            var now = DateTimeOffset.UtcNow;

            check("未开通本地账号的人员不能签发一次性嵌入令牌",
                await FailsUnauthorizedAsync(() => identities.CreateEmbedTokenAsync(
                    "demo", "missing", "p3", "#/m/tasks"), ErrorCodes.NotFound));

            var local = await identities.LoginAsync("E1", "password-1");
            var pendingEmbed = await identities.CreateEmbedTokenAsync("demo", "external", "p1", "#/m/tasks");
            var embed = await identities.ExchangeEmbedTokenAsync((await identities.CreateEmbedTokenAsync(
                "demo", "external", "p1", "#/m/tasks")).Token);
            var inactive = await organization.SaveTrustedImportAsync(new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "p1", EmployeeNumber = "E1", Name = "甲", IsActive = false }],
            }, "administrator", now);
            var active = await organization.SaveTrustedImportAsync(new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "p1", EmployeeNumber = "E1", Name = "甲" }],
            }, "administrator", now.AddMinutes(1));
            check("可信导入离职再复职单调增加账号代际，旧本地和嵌入令牌不复活",
                inactive.Issues.Count == 0 && active.Issues.Count == 0 &&
                await TokenVersionAsync(factory, "p1") == 2 &&
                !await identities.ValidateAccessPrincipalAsync("p1", 0, IdentitySessionKinds.Local, null, null) &&
                !await identities.ValidateAccessPrincipalAsync("p1", 0, IdentitySessionKinds.Embed, "demo", "external") &&
                await FailsUnauthorizedAsync(() => identities.RefreshAsync(local.RefreshToken), ErrorCodes.Unauthorized) &&
                await FailsUnauthorizedAsync(() => identities.RefreshAsync(embed.RefreshToken), ErrorCodes.Unauthorized) &&
                await FailsUnauthorizedAsync(() => identities.ExchangeEmbedTokenAsync(pendingEmbed.Token), ErrorCodes.Unauthorized));

            var afterRehire = await identities.LoginAsync("E1", "password-1");
            var beforeRebind = await identities.CreateEmbedTokenAsync("demo", "external", "p1", "#/m/tasks");
            var beforeRebindSession = await identities.ExchangeEmbedTokenAsync((await identities.CreateEmbedTokenAsync(
                "demo", "external", "p1", "#/m/tasks")).Token);
            await organization.BindPersonAsync(new ExternalPersonBindingEntity
            {
                ClientAppCode = "demo",
                ExternalUserId = "external",
                PersonId = "p2",
                Method = "manual",
            }, "administrator", now.AddMinutes(2));
            var p2Embed = await identities.CreateEmbedTokenAsync("demo", "external", "p2", "#/m/tasks");
            await organization.BindPersonAsync(new ExternalPersonBindingEntity
            {
                ClientAppCode = "demo",
                ExternalUserId = "external",
                PersonId = "p1",
                Method = "manual",
            }, "administrator", now.AddMinutes(3));
            check("人员绑定甲到乙再到甲会递增双方账号代际并拒绝旧嵌入交换与刷新",
                await TokenVersionAsync(factory, "p1") == 4 && await TokenVersionAsync(factory, "p2") == 2 &&
                !await identities.ValidateAccessPrincipalAsync("p1", 2, IdentitySessionKinds.Local, null, null) &&
                !await identities.ValidateAccessPrincipalAsync("p1", 2, IdentitySessionKinds.Embed, "demo", "external") &&
                await FailsUnauthorizedAsync(() => identities.RefreshAsync(afterRehire.RefreshToken), ErrorCodes.Unauthorized) &&
                await FailsUnauthorizedAsync(() => identities.RefreshAsync(beforeRebindSession.RefreshToken), ErrorCodes.Unauthorized) &&
                await FailsUnauthorizedAsync(() => identities.ExchangeEmbedTokenAsync(beforeRebind.Token), ErrorCodes.Unauthorized) &&
                await FailsUnauthorizedAsync(() => identities.ExchangeEmbedTokenAsync(p2Embed.Token), ErrorCodes.Unauthorized));

            await organization.BindPersonAsync(new ExternalPersonBindingEntity
            {
                ClientAppCode = "demo",
                ExternalUserId = "external",
                PersonId = "p1",
                Method = "import",
            }, "administrator", now.AddMinutes(4));
            check("同一人员绑定方式更新不作废其会话", await TokenVersionAsync(factory, "p1") == 4);

            var importEmbed = await identities.CreateEmbedTokenAsync("demo", "external", "p1", "#/m/tasks");
            var importAway = await organization.SaveTrustedImportAsync(new OrganizationImportBatch
            {
                PersonBindings = [new ExternalPersonBindingEntity
                {
                    ClientAppCode = "demo", ExternalUserId = "external", PersonId = "p2", Method = "import",
                }],
            }, "administrator", now.AddMinutes(5));
            var importBack = await organization.SaveTrustedImportAsync(new OrganizationImportBatch
            {
                PersonBindings = [new ExternalPersonBindingEntity
                {
                    ClientAppCode = "demo", ExternalUserId = "external", PersonId = "p1", Method = "import",
                }],
            }, "administrator", now.AddMinutes(6));
            check("可信导入绑定甲到乙再到甲也作废原有一次性嵌入令牌",
                importAway.Issues.Count == 0 && importBack.Issues.Count == 0 &&
                await TokenVersionAsync(factory, "p1") == 6 && await TokenVersionAsync(factory, "p2") == 4 &&
                await FailsUnauthorizedAsync(() => identities.ExchangeEmbedTokenAsync(importEmbed.Token), ErrorCodes.Unauthorized));

            var pushLogin = await identities.LoginAsync("E2", "password-2");
            var departed = await organization.SavePushedChangesAsync(new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "p2", EmployeeNumber = "E2", Name = "乙", IsActive = false }],
            }, "demo", "demo", now.AddMinutes(7));
            var rehired = await organization.SavePushedChangesAsync(new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "p2", EmployeeNumber = "E2", Name = "乙" }],
            }, "demo", "demo", now.AddMinutes(8));
            check("可信推送离职再复职单调增加账号代际，旧访问和刷新令牌不复活",
                departed.Issues.Count == 0 && rehired.Issues.Count == 0 &&
                await TokenVersionAsync(factory, "p2") == 6 &&
                !await identities.ValidateAccessPrincipalAsync("p2", 4, IdentitySessionKinds.Local, null, null) &&
                await FailsUnauthorizedAsync(() => identities.RefreshAsync(pushLogin.RefreshToken), ErrorCodes.Unauthorized));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 读取当前持久化账号代际。
    /// </summary>
    /// <param name="factory">数据库上下文工厂。</param>
    /// <param name="personId">人员 ID。</param>
    /// <returns>当前账号版本。</returns>
    private static async Task<long> TokenVersionAsync(CheckContextFactory factory, string personId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LocalAccounts.Where(item => item.PersonId == personId)
            .Select(item => item.TokenVersion).SingleAsync();
    }

    /// <summary>
    /// 检查身份操作是否被预期错误码拒绝。
    /// </summary>
    /// <param name="action">待执行操作。</param>
    /// <param name="code">预期错误码。</param>
    /// <returns>错误码符合预期为真。</returns>
    private static async Task<bool> FailsUnauthorizedAsync(Func<Task> action, string code)
    {
        try
        {
            await action();
            return false;
        }
        catch (UserFriendlyException error)
        {
            return error.Code == code;
        }
    }

    /// <summary>
    /// 检查专用数据库上下文工厂。
    /// </summary>
    /// <param name="options">SQLite 模型配置。</param>
    private sealed class CheckContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 以确定性摘要隔离密码算法耗时，场景重点是令牌与组织事务。
    /// </summary>
    private sealed class CheckPasswordService : IIdentityPasswordService
    {
        /// <inheritdoc />
        public PasswordDigest Hash(string password) => new() { Hash = "hash:" + password, Salt = "salt" };

        /// <inheritdoc />
        public bool Verify(string password, string hash, string salt) =>
            hash == "hash:" + password && salt == "salt";

        /// <inheritdoc />
        public bool VerifyMissing(string password) => false;
    }
}
