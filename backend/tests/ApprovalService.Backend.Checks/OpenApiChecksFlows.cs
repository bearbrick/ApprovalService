using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实流程版本检查开放流程列表和审批数据定义的权限边界。
/// </summary>
internal static class OpenApiChecksFlows
{
    /// <summary>
    /// 检查只返回已发布且双向授权的流程，以及字段样例。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-flow-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "flows.db")).Options;
            var factory = new FlowContextFactory(options);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                AddFlow(db, "allowed", "demo", published: true);
                AddFlow(db, "other-app", "another", published: true);
                AddFlow(db, "draft", "demo", published: false);
                await db.SaveChangesAsync();
            }

            var app = new ClientAppEntity
            {
                Code = "demo",
                AllowedFlowCodesJson = "[\"allowed\",\"other-app\",\"draft\"]",
            };
            var service = new FlowCatalogAppService(new FlowCatalogRepository(factory));
            var available = await service.ListAsync(app);
            check("开放流程列表只含发布且授权的版本", available.Count == 1 &&
                available[0].Code == "allowed" && available[0].Version == 1);

            var schema = await service.GetSchemaAsync(app, "allowed");
            check("审批数据定义给出与当前版本一致的 JSON 样例", schema.FlowCode == "allowed" &&
                schema.Data.Fields.Count == 1 && schema.Example.TryGetProperty("amount", out _));

            var forbidden = false;
            try
            {
                await service.GetSchemaAsync(app, "other-app");
            }
            catch (UserFriendlyException error) when (error.Code == "FORBIDDEN")
            {
                forbidden = true;
            }

            check("流程自身限制的其他应用不能读取审批字段", forbidden);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 写入供目录查询使用的流程和发布快照。
    /// </summary>
    /// <param name="db">临时数据库。</param>
    /// <param name="code">流程编码。</param>
    /// <param name="allowedApp">流程定义允许的接入应用。</param>
    /// <param name="published">是否发布。</param>
    private static void AddFlow(ApprovalDbContext db, string code, string allowedApp, bool published)
    {
        var flowId = code + "-id";
        var versionId = code + "-version";
        db.Flows.Add(new FlowEntity
        {
            Id = flowId,
            Code = code,
            Name = code,
            PublishedVersionId = published ? versionId : null,
        });
        db.FlowVersions.Add(new FlowVersionEntity
        {
            Id = versionId,
            FlowId = flowId,
            Version = 1,
            SchemaVersion = 1,
            DefinitionJson = JsonSerializer.Serialize(new FlowDefinition
            {
                AllowedClientApps = [allowedApp],
                Data = new FlowDataDefinition
                {
                    Fields = [new FlowFieldDefinition { Key = "amount", Name = "金额", Type = FlowFieldTypes.Number }],
                },
            }, ApprovalJson.Options),
            GraphJson = "{}",
        });
    }

    /// <summary>
    /// 为单个检查隔离数据库上下文。
    /// </summary>
    /// <param name="options">SQLite 配置。</param>
    private sealed class FlowContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
