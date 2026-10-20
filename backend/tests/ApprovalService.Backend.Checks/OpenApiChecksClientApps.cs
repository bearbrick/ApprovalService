using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 校验接入应用密钥的生成、加密与鉴权。
/// </summary>
internal static class OpenApiChecksClientApps
{
    /// <summary>
    /// 检查 API Key 只存摘要、签名密钥加密以及无效 Key 拒绝路径。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-client-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "client-app.db"))
                .Options;
            var factory = new ClientAppContextFactory(options);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            }).Build();
            var protector = new AesIntegrationSecretProtector(configuration);
            IClientAppRepository repository = new ClientAppRepository(factory);
            var service = new ClientAppAppService(repository, protector);
            var secrets = await service.CreateAsync(new CreateClientAppInput
            {
                Code = "demo",
                Name = "示例业务系统",
                AllowedFlowCodes = ["PURCHASE"],
                CallbackUrl = "https://example.invalid/callback",
            });
            var saved = await repository.FindByCodeAsync("demo");
            check("API Key 只保存 SHA-256 摘要且不能从数据库反查明文", saved is not null &&
                saved.ApiKeyHash == ClientAppAppService.HashKey(secrets.ApiKey) &&
                saved.ApiKeyHash != secrets.ApiKey && saved.ApiKeyHash.Length == 64);
            check("回调签名密钥加密落库并可由主密钥解密", saved is not null &&
                saved.SigningSecretCiphertext != secrets.SigningSecret &&
                protector.Unprotect(saved.SigningSecretCiphertext) == secrets.SigningSecret);
            check("接入应用流程授权写入持久化配置", saved is not null &&
                JsonSerializer.Deserialize<string[]>(saved.AllowedFlowCodesJson)?.SequenceEqual(["PURCHASE"]) == true);
            check("有效 API Key 解析到已启用应用", (await service.AuthenticateAsync(secrets.ApiKey)).Code == "demo");

            var unauthorized = false;
            try
            {
                await service.AuthenticateAsync("incorrect-key");
            }
            catch (UserFriendlyException error) when (error.Code == "UNAUTHORIZED")
            {
                unauthorized = true;
            }

            check("无效 API Key 不能调用开放接口", unauthorized);

            var invalidFlows = false;
            try
            {
                await service.CreateAsync(new CreateClientAppInput
                {
                    Code = "invalid-null-flows",
                    Name = "错误配置",
                    AllowedFlowCodes = null!,
                });
            }
            catch (UserFriendlyException error) when (error.Code == "VALIDATION_FAILED")
            {
                invalidFlows = true;
            }

            check("显式空流程授权配置返回可纠正错误", invalidFlows);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 按测试用例创建短生命周期数据库上下文。
    /// </summary>
    /// <param name="options">临时 SQLite 配置。</param>
    private sealed class ClientAppContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
