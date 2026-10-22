using System.Globalization;
using System.Security.Cryptography;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Identity;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 检查账号令牌、改密作废及一次性嵌入。
/// </summary>
internal static class IdentityAppServiceChecks
{
    /// <summary>
    /// 执行身份服务的主要场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "identity.db"))
                .Options;
            var factory = new IdentityContextFactory(options);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.People.Add(new PersonEntity
                {
                    Id = "person-1",
                    EmployeeNumber = "EMP-001",
                    Name = "审批人",
                });
                db.People.Add(new PersonEntity
                {
                    Id = "person-2",
                    EmployeeNumber = "EMP-002",
                    Name = "新审批人",
                });
                db.LocalAccounts.Add(new LocalAccountEntity
                {
                    PersonId = "person-1",
                    PasswordHash = "hash:initial-password",
                    PasswordSalt = "salt",
                    MustChangePassword = true,
                });
                db.ClientApps.Add(new ClientAppEntity
                {
                    Code = "demo",
                    Name = "演示系统",
                    ApiKeyHash = ClientAppAppService.HashKey("initial-key"),
                    SigningSecretCiphertext = "test-ciphertext",
                });
                db.ExternalPersonBindings.Add(new ExternalPersonBindingEntity
                {
                    ClientAppCode = "demo",
                    ExternalUserId = "external-1",
                    PersonId = "person-1",
                    Method = "test",
                });
                await db.SaveChangesAsync();
            }

            var clock = new IdentityClock(new DateTimeOffset(2026, 10, 20, 4, 0, 0, TimeSpan.Zero));
            var repository = new IdentityRepository(factory);
            var jwt = new JwtIdentityTokenService(Options.Create(new IdentityTokenOptions
            {
                SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            }), Options.Create(new DataOptions { Directory = directory }));
            var service = new IdentityAppService(repository, new CheckPasswordService(), jwt, clock);

            await service.ProvisionAccountAsync("person-2", "initial-password-2");
            var firstLogin = await service.LoginAsync("EMP-002", "initial-password-2");
            check("管理员开通在职人员账号后必须首次改密，不能重复开通",
                firstLogin.MustChangePassword &&
                await FailsWithCodeAsync(() => service.ProvisionAccountAsync("person-2", "initial-password-2"),
                    ErrorCodes.Conflict));

            var login = await service.LoginAsync("EMP-001", "initial-password");
            check("账号登录签发短期访问令牌与刷新令牌且提示首次改密",
                login.PersonId == "person-1" && login.AccessToken.Length > 80 &&
                login.RefreshToken.Length >= 40 && login.MustChangePassword &&
                login.AccessExpiresAt == clock.GetUtcNow().AddMinutes(15) &&
                login.RefreshExpiresAt == clock.GetUtcNow().AddDays(30));
            var localJwt = new JsonWebTokenHandler().ReadJsonWebToken(login.AccessToken);
            check("本地登录令牌声明本地会话且不带接入应用",
                localJwt.TryGetPayloadValue(JwtIdentityTokenService.SessionKindClaim, out string? localKind) &&
                localKind == JwtIdentityTokenService.LocalSessionKind &&
                !localJwt.TryGetPayloadValue(JwtIdentityTokenService.ClientAppCodeClaim, out string? _));
            check("密码错误与不存在工号均返回未授权",
                await FailsWithCodeAsync(() => service.LoginAsync("EMP-001", "wrong"), ErrorCodes.Unauthorized) &&
                await FailsWithCodeAsync(() => service.LoginAsync("MISSING", "wrong"), ErrorCodes.Unauthorized));
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("数据库只保存刷新令牌摘要，不保存原始令牌",
                    await db.RefreshTokens.CountAsync() == 2 &&
                    await db.RefreshTokens.AllAsync(item => item.TokenHash.Length == 64 &&
                        item.TokenHash != login.RefreshToken));
            }

            var refreshed = await service.RefreshAsync(login.RefreshToken);
            check("刷新令牌轮换成功且旧令牌不能复用",
                refreshed.RefreshToken != login.RefreshToken &&
                await FailsWithCodeAsync(() => service.RefreshAsync(login.RefreshToken), ErrorCodes.Unauthorized));
            await service.ChangePasswordAsync("person-1", "initial-password", "new-password-123");
            check("改密使旧访问和刷新令牌失效",
                !await service.ValidateAccessPrincipalAsync("person-1", 0, IdentitySessionKinds.Local, null, null) &&
                await FailsWithCodeAsync(() => service.RefreshAsync(refreshed.RefreshToken), ErrorCodes.Unauthorized));
            var changedLogin = await service.LoginAsync("EMP-001", "new-password-123");
            check("首次改密后允许新密码登录且清除改密标记", !changedLogin.MustChangePassword &&
                await FailsWithCodeAsync(() => service.LoginAsync("EMP-001", "initial-password"), ErrorCodes.Unauthorized));

            check("嵌入令牌拒绝外站路径", await FailsWithCodeAsync(() => service.CreateEmbedTokenAsync(
                "demo", "external-1", "person-1", "https://outside.example/"), ErrorCodes.ValidationFailed));
            var embed = await service.CreateEmbedTokenAsync("demo", "external-1", "person-1", "#/m/tasks");
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("一次性嵌入令牌仅以摘要保存",
                    await db.EmbedTokens.CountAsync() == 1 &&
                    await db.EmbedTokens.AllAsync(item => item.TokenHash.Length == 64 &&
                        item.TokenHash != embed.Token));
            }

            var exchanged = await service.ExchangeEmbedTokenAsync(embed.Token);
            check("嵌入交换绑定人员与站内路径且只能消费一次",
                exchanged.PersonId == "person-1" && exchanged.TargetPath == "#/m/tasks" &&
                await FailsWithCodeAsync(() => service.ExchangeEmbedTokenAsync(embed.Token), ErrorCodes.Unauthorized));
            var embedJwt = new JsonWebTokenHandler().ReadJsonWebToken(exchanged.AccessToken);
            var initialGeneration = ReadAuthGeneration(embedJwt);
            var embedRefreshed = await service.RefreshAsync(exchanged.RefreshToken);
            var refreshedEmbedJwt = new JsonWebTokenHandler().ReadJsonWebToken(embedRefreshed.AccessToken);
            check("嵌入会话及刷新令牌始终绑定原接入应用，不能升级成本地管理员会话",
                IsEmbedJwt(embedJwt, "demo", "external-1") &&
                IsEmbedJwt(refreshedEmbedJwt, "demo", "external-1") && initialGeneration == 1);
            var expiring = await service.CreateEmbedTokenAsync("demo", "external-1", "person-1", "#/m/tasks");
            clock.Advance(TimeSpan.FromMinutes(6));
            check("嵌入令牌过期后不能交换",
                await FailsWithCodeAsync(() => service.ExchangeEmbedTokenAsync(expiring.Token), ErrorCodes.Unauthorized));

            await using (var db = await factory.CreateDbContextAsync())
            {
                var binding = await db.ExternalPersonBindings.SingleAsync();
                binding.PersonId = "person-2";
                await db.SaveChangesAsync();
            }

            check("外部人员改绑后旧嵌入访问与刷新令牌均失效",
                !await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Embed,
                    "demo", "external-1", initialGeneration) &&
                await FailsWithCodeAsync(() => service.RefreshAsync(embedRefreshed.RefreshToken),
                    ErrorCodes.Unauthorized));
            await using (var db = await factory.CreateDbContextAsync())
            {
                var binding = await db.ExternalPersonBindings.SingleAsync();
                binding.PersonId = "person-1";
                await db.SaveChangesAsync();
            }

            var pendingBeforeDisable = await service.CreateEmbedTokenAsync(
                "demo", "external-1", "person-1", "#/m/tasks");
            var beforeDisable = await service.ExchangeEmbedTokenAsync((await service.CreateEmbedTokenAsync(
                "demo", "external-1", "person-1", "#/m/tasks")).Token);
            var localBeforeDisable = await service.LoginAsync("EMP-001", "new-password-123");
            var apps = new ClientAppAppService(new ClientAppRepository(factory), new CheckSecretProtector());
            await apps.SetEnabledAsync("demo", false);

            check("接入应用停用后旧嵌入访问与刷新令牌均失效",
                !await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Embed,
                    "demo", "external-1", initialGeneration) &&
                await FailsWithCodeAsync(() => service.CreateEmbedTokenAsync(
                    "demo", "external-1", "person-1", "#/m/tasks"), ErrorCodes.Forbidden));
            await apps.SetEnabledAsync("demo", true);
            check("接入应用停用再启用后旧访问、刷新和未兑换嵌入令牌永久失效",
                await ReadGenerationAsync(factory) == 3 &&
                !await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Embed,
                    "demo", "external-1", initialGeneration) &&
                await FailsWithCodeAsync(() => service.RefreshAsync(beforeDisable.RefreshToken),
                    ErrorCodes.Unauthorized) &&
                await FailsWithCodeAsync(() => service.ExchangeEmbedTokenAsync(pendingBeforeDisable.Token),
                    ErrorCodes.Unauthorized));
            check("应用启停不作废同一人员的本地登录与刷新",
                await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Local, null, null) &&
                (await service.RefreshAsync(localBeforeDisable.RefreshToken)).PersonId == "person-1");

            var pendingBeforeRotation = await service.CreateEmbedTokenAsync(
                "demo", "external-1", "person-1", "#/m/tasks");
            var beforeRotation = await service.ExchangeEmbedTokenAsync((await service.CreateEmbedTokenAsync(
                "demo", "external-1", "person-1", "#/m/tasks")).Token);
            var currentGeneration = ReadAuthGeneration(new JsonWebTokenHandler().ReadJsonWebToken(
                beforeRotation.AccessToken));
            check("新嵌入会话包含新代际且缺失代际声明不能访问",
                currentGeneration == 3 &&
                await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Embed,
                    "demo", "external-1", currentGeneration) &&
                !await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Embed,
                    "demo", "external-1"));
            var rotated = await apps.RotateSecretsAsync("demo");
            check("密钥轮换原子递增代际并作废旧 Key 与旧嵌入令牌",
                await ReadGenerationAsync(factory) == 4 &&
                await FailsWithCodeAsync(() => apps.AuthenticateAsync("initial-key"), ErrorCodes.Unauthorized) &&
                (await apps.AuthenticateAsync(rotated.ApiKey)).Code == "demo" &&
                !await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Embed,
                    "demo", "external-1", currentGeneration) &&
                await FailsWithCodeAsync(() => service.RefreshAsync(beforeRotation.RefreshToken),
                    ErrorCodes.Unauthorized) &&
                await FailsWithCodeAsync(() => service.ExchangeEmbedTokenAsync(pendingBeforeRotation.Token),
                    ErrorCodes.Unauthorized));
            var afterRotation = await service.ExchangeEmbedTokenAsync((await service.CreateEmbedTokenAsync(
                "demo", "external-1", "person-1", "#/m/tasks")).Token);
            check("密钥轮换后的新嵌入访问和刷新仍可用",
                ReadAuthGeneration(new JsonWebTokenHandler().ReadJsonWebToken(afterRotation.AccessToken)) == 4 &&
                await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Embed,
                    "demo", "external-1", 4) &&
                (await service.RefreshAsync(afterRotation.RefreshToken)).PersonId == "person-1");

            await service.RevokeAllAsync("person-1");
            check("管理员作废令牌版本后旧访问令牌无效",
                !await service.ValidateAccessPrincipalAsync("person-1", 1, IdentitySessionKinds.Local, null, null));

            await service.LoginAsync("EMP-001", "new-password-123");

            for (var failure = 0; failure < 5; failure++)
            {
                await FailsWithCodeAsync(() => service.LoginAsync("EMP-001", "wrong"), ErrorCodes.Unauthorized);
            }

            check("连续五次密码错误锁定账号且正确密码也不能绕过",
                await FailsWithCodeAsync(() => service.LoginAsync("EMP-001", "new-password-123"),
                    ErrorCodes.Unauthorized));
            clock.Advance(TimeSpan.FromMinutes(16));
            var unlocked = await service.LoginAsync("EMP-001", "new-password-123");
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("锁定期结束后正确登录清除失败计数", unlocked.PersonId == "person-1" &&
                    await db.LocalAccounts.AnyAsync(item => item.PersonId == "person-1" &&
                        item.FailedLogins == 0 && item.LockedUntil == null));
            }

            await IdentityLifecycleChecks.RunAsync(check);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 检查应用服务是否按指定可纠正错误拒绝操作。
    /// </summary>
    /// <param name="action">异步操作。</param>
    /// <param name="code">预期错误码。</param>
    /// <returns>错误码匹配为真。</returns>
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
    /// 检查签发令牌明确携带嵌入来源与应用编码。
    /// </summary>
    /// <param name="token">已读取的 JWT。</param>
    /// <param name="clientAppCode">预期接入应用编码。</param>
    /// <param name="externalUserId">预期外部人员 ID。</param>
    /// <returns>来源与应用编码均一致为真。</returns>
    private static bool IsEmbedJwt(JsonWebToken token, string clientAppCode, string externalUserId) =>
        token.TryGetPayloadValue(JwtIdentityTokenService.SessionKindClaim, out string? kind) &&
        kind == JwtIdentityTokenService.EmbedSessionKind &&
        token.TryGetPayloadValue(JwtIdentityTokenService.ClientAppCodeClaim, out string? code) &&
        code == clientAppCode &&
        token.TryGetPayloadValue(JwtIdentityTokenService.ExternalUserIdClaim, out string? userId) &&
        userId == externalUserId && ReadAuthGeneration(token) > 0;

    /// <summary>
    /// 从真实签发的访问令牌读取应用身份代际。
    /// </summary>
    /// <param name="token">已读取的 JWT。</param>
    /// <returns>有效代际；缺失或格式错误时为零。</returns>
    private static long ReadAuthGeneration(JsonWebToken token) =>
        token.TryGetPayloadValue(JwtIdentityTokenService.AuthGenerationClaim, out string? value) &&
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            ? generation : 0;

    /// <summary>
    /// 读取数据库中的应用身份代际。
    /// </summary>
    /// <param name="factory">检查专用数据库工厂。</param>
    /// <returns>当前代际。</returns>
    private static async Task<long> ReadGenerationAsync(IdentityContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ClientApps.Select(item => item.AuthGeneration).SingleAsync();
    }

    /// <summary>
    /// 为检查程序创建独立数据库上下文。
    /// </summary>
    /// <param name="options">SQLite 配置。</param>
    private sealed class IdentityContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 将安全哈希服务隔离，以便场景只验证业务与数据库原子性。
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

    /// <summary>
    /// 检查专用的可逆加密替身，避免依赖部署主密钥。
    /// </summary>
    private sealed class CheckSecretProtector : IIntegrationSecretProtector
    {
        /// <inheritdoc />
        public string Protect(string secret) => "check:" + secret;

        /// <inheritdoc />
        public string Unprotect(string ciphertext) => ciphertext[6..];
    }

    /// <summary>
    /// 可手动推进的令牌检查时间来源。
    /// </summary>
    /// <param name="now">初始 UTC 时间。</param>
    private sealed class IdentityClock(DateTimeOffset now) : TimeProvider
    {
        /// <summary>
        /// 当前 UTC 时间。
        /// </summary>
        private DateTimeOffset current = now;

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => current;

        /// <summary>
        /// 手动推进时间。
        /// </summary>
        /// <param name="duration">要前进的时长。</param>
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
