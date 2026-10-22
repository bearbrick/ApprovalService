using System.Security.Cryptography;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 只供统一验证脚本使用的临时种库命令，不向运行中的服务注册入口。
/// </summary>
internal static class SdkLiveFixture
{
    /// <summary>
    /// 测试流程编码，调用端检查与此值保持一致。
    /// </summary>
    private const string FlowCode = "SDK_E2E";

    /// <summary>
    /// 在全新目录内迁移数据库并写入单次运行的测试身份与流程。
    /// </summary>
    /// <param name="dataDirectory">本次验证专用的空数据目录。</param>
    /// <param name="secretFile">同一目录下的一次性密钥文件。</param>
    /// <returns>种库完成时结束。</returns>
    internal static async Task SeedAsync(string dataDirectory, string secretFile)
    {
        var directory = Path.GetFullPath(dataDirectory);
        var output = Path.GetFullPath(secretFile);
        if (!string.Equals(Path.GetDirectoryName(output), directory, StringComparison.OrdinalIgnoreCase) ||
            (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()))
        {
            throw new ArgumentException("SDK 测试种库需要全新空目录，密钥文件必须在该目录内。");
        }

        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "approval.db");
        var options = new DbContextOptionsBuilder<SqliteApprovalDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database }.ToString()).Options;
        await using var db = new SqliteApprovalDbContext(options);
        await db.Database.MigrateAsync();

        // 每次运行独立生成应用密钥和加密主密钥，数据库里只存摘要和密文。
        var apiKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApprovalService:Integration:MasterKey"] = masterKey,
        }).Build();
        var protector = new AesIntegrationSecretProtector(configuration);
        db.ClientApps.Add(new ClientAppEntity
        {
            Code = "sdk-e2e",
            Name = "SDK 临时验证应用",
            ApiKeyHash = ClientAppAppService.HashKey(apiKey),
            SigningSecretCiphertext = protector.Protect(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))),
            AllowedFlowCodesJson = JsonSerializer.Serialize(new[] { FlowCode }),
        });

        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        var approval = graph.Nodes.Single(node => node.Id == "n1");
        approval.Name = "验证审批";
        approval.Assignee = new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "approver" };
        var definition = new FlowDefinition
        {
            Code = FlowCode,
            Name = "SDK 真实服务验证",
            AllowedClientApps = ["sdk-e2e"],
        };
        db.Flows.Add(new FlowEntity
        {
            Id = "sdk-flow",
            Code = FlowCode,
            Name = definition.Name,
            PublishedVersionId = "sdk-version",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.FlowVersions.Add(new FlowVersionEntity
        {
            Id = "sdk-version",
            FlowId = "sdk-flow",
            Version = 1,
            SchemaVersion = FlowSchema.CurrentVersion,
            DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
            GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
            PublishedAt = DateTimeOffset.UtcNow,
        });

        db.Departments.Add(new DepartmentEntity { Id = "sdk-department", Code = "SDK", Name = "验证部门" });
        db.People.AddRange(
            new PersonEntity { Id = "requester", EmployeeNumber = "SDK001", Name = "验证发起人" },
            new PersonEntity { Id = "approver", EmployeeNumber = "SDK002", Name = "验证审批人" });
        db.PersonDepartments.AddRange(
            new PersonDepartmentEntity { PersonId = "requester", DepartmentId = "sdk-department", IsPrimary = true },
            new PersonDepartmentEntity { PersonId = "approver", DepartmentId = "sdk-department", IsPrimary = true });
        db.ExternalPersonBindings.AddRange(
            new ExternalPersonBindingEntity
            {
                ClientAppCode = "sdk-e2e",
                ExternalUserId = "alice",
                PersonId = "requester",
                Method = "manual",
            },
            new ExternalPersonBindingEntity
            {
                ClientAppCode = "sdk-e2e",
                ExternalUserId = "bob",
                PersonId = "approver",
                Method = "manual",
            });
        await db.SaveChangesAsync();
        // 密钥只保存在本次验证的临时目录，脚本结束时与数据库一同删除。
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, new
        {
            apiKey,
            masterKey,
            flowCode = FlowCode,
            initiator = "alice",
            approver = "bob",
        });
    }
}
