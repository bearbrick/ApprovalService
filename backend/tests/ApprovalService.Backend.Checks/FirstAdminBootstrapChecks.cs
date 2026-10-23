using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Identity;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 验证服务器本机首位管理员初始化的原子性和一次性。
/// </summary>
internal static class FirstAdminBootstrapChecks
{
    /// <summary>
    /// 运行首次开通与已存在人员的场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApprovalDbContext>().UseSqlite(connection).Options;
        var factory = new BootstrapContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var bootstrap = new FirstAdminBootstrapper(factory, new CheckPasswordService(),
            Options.Create(new DataOptions()));
        check("首次开通缺少新人员工号时不写入任何数据",
            await FailsWithCodeAsync(() => bootstrap.BootstrapAsync("admin", null, "管理员", "initial-secret"),
                ErrorCodes.ValidationFailed));
        await bootstrap.BootstrapAsync("admin", "A001", "管理员", "initial-secret");
        await using (var db = await factory.CreateDbContextAsync())
        {
            var person = await db.People.AsNoTracking().SingleAsync();
            var account = await db.LocalAccounts.AsNoTracking().SingleAsync();
            check("本机首次开通在同一事务创建人员、账号和管理员角色",
                person.Id == "admin" && person.EmployeeNumber == "A001" &&
                account.PersonId == person.Id && account.MustChangePassword &&
                account.PasswordHash == "hash:initial-secret" &&
                await db.PersonRoles.AnyAsync(item => item.PersonId == "admin" &&
                    item.RoleCode == "organization-admin") &&
                await db.OrganizationRoles.AnyAsync(item => item.Code == "organization-admin"));
            check("首次开通的人员及角色变更留痕且不保存明文密码",
                await db.OrganizationChanges.CountAsync() == 2 &&
                !await db.OrganizationChanges.AnyAsync(item => item.AfterJson != null &&
                    item.AfterJson.Contains("initial-secret")));
        }

        check("已有管理员后再次运行本机初始化被拒绝",
            await FailsWithCodeAsync(() => bootstrap.BootstrapAsync("second", "A002", "第二人",
                "second-secret"), ErrorCodes.Conflict));
        await using (var db = await factory.CreateDbContextAsync())
        {
            check("重复初始化不会留下第二个人员或账号",
                await db.People.CountAsync() == 1 && await db.LocalAccounts.CountAsync() == 1);
        }

        await using var existingConnection = new SqliteConnection("Data Source=:memory:");
        await existingConnection.OpenAsync();
        var existingOptions = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseSqlite(existingConnection).Options;
        var existingFactory = new BootstrapContextFactory(existingOptions);
        await using (var db = await existingFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.People.Add(new PersonEntity { Id = "existing", EmployeeNumber = "E001", Name = "已导入人员" });
            await db.SaveChangesAsync();
        }

        var existingBootstrap = new FirstAdminBootstrapper(existingFactory, new CheckPasswordService(),
            Options.Create(new DataOptions()));
        await existingBootstrap.BootstrapAsync("existing", null, null, "existing-secret");
        await using (var db = await existingFactory.CreateDbContextAsync())
        {
            check("已导入在职人员可凭内部 ID 开通首位管理员且保留原人员资料",
                await db.People.CountAsync() == 1 &&
                await db.PersonRoles.AnyAsync(item => item.PersonId == "existing" &&
                    item.RoleCode == "organization-admin") &&
                await db.LocalAccounts.AnyAsync(item => item.PersonId == "existing" &&
                    item.MustChangePassword));
        }
    }

    /// <summary>
    /// 检查可纠正错误的代码。
    /// </summary>
    /// <param name="action">待运行的异步操作。</param>
    /// <param name="code">期望错误码。</param>
    /// <returns>错误码一致为真。</returns>
    private static async Task<bool> FailsWithCodeAsync(Func<Task> action, string code)
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
    /// 为独立的 SQLite 连接创建检查上下文。
    /// </summary>
    /// <param name="options">数据库连接选项。</param>
    private sealed class BootstrapContextFactory(DbContextOptions<ApprovalDbContext> options)
        : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 用可预测摘要验证初始化事务，不暴露真实密码算法细节。
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
