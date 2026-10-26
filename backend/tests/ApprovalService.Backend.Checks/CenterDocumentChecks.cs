using System.Text.Json;
using ApprovalService.Application.Administration;
using ApprovalService.Application.Center;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 授权投影验证原单签名钩子的调用范围和地址边界。
/// </summary>
internal static class CenterDocumentChecks
{
    /// <summary>
    /// 检查唯一人员绑定、可见字段、期限、来源和管理配置。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">结果记录器。</param>
    /// <returns>异步检查。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            await using var server = new ServerFactory(backendRoot, directory, new Dictionary<string, string?>
            {
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            });
            using var http = server.CreateClient();
            using var ready = await http.GetAsync(ApiRoutes.HealthReady);
            await CenterChecksFixture.SeedAsync(server.Services);
            using var scope = server.Services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                var version = await db.FlowVersions.AsNoTracking().SingleAsync(item => item.Id == "version");
                var definition = JsonSerializer.Deserialize<FlowDefinition>(version.DefinitionJson, ApprovalJson.Options)!;
                definition.DetailLayout.Mode = "embed";
                definition.DetailLayout.EmbedPageCode = "purchase-readonly";
                // 发布版本只追加；检查夹具新增版本后将目标审批单指向它。
                db.FlowVersions.Add(new ApprovalService.Domain.Runtime.FlowVersionEntity
                {
                    Id = "version-embed",
                    FlowId = version.FlowId,
                    Version = 3,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
                    GraphJson = version.GraphJson,
                });
                var instance = await db.Instances.SingleAsync(item => item.Id == "a-todo");
                instance.FlowVersionId = "version-embed";
                await db.SaveChangesAsync();
            }
            var settings = scope.ServiceProvider.GetRequiredService<AdminDocumentSettingsAppService>();
            var configured = await settings.SetAsync("one", new UpdateAdminDocumentSettingsRequest
            {
                DocumentUrl = "https://erp.example.com/hooks/document",
                AllowedDocumentOrigins = ["https://documents.example.com"],
                Reason = "接入只读原单",
            }, "stranger", DateTimeOffset.UtcNow);
            check("原单管理设置只接受完整 HTTPS 子页面来源并保存签名钩子",
                configured.DocumentUrl == "https://erp.example.com/hooks/document" &&
                configured.AllowedDocumentOrigins.SequenceEqual(["https://documents.example.com"]));
            var repository = scope.ServiceProvider.GetRequiredService<ICenterQueryRepository>();
            var apps = scope.ServiceProvider.GetRequiredService<IClientAppRepository>();
            var hook = new DocumentHookStub();
            var service = new CenterDocumentAppService(repository, apps, hook, TimeProvider.System);
            var person = new CenterScope { PersonId = "viewer" };
            var result = await service.GetAsync(person, "a-todo");
            check("原单请求使用锁定版本页面编码、当前绑定和裁剪后的字段路径",
                result.Url == hook.Response.Url && hook.Request is
                {
                    EmbedPageCode: "purchase-readonly",
                    ViewerExternalId: "external", BusinessKey: "B-a-todo", ReadOnly: true
                } &&
                hook.Request.VisibleFieldPaths.Contains("amount") &&
                hook.Request.VisibleFieldPaths.Contains("lines.qty") &&
                !hook.Request.VisibleFieldPaths.Contains("lines") &&
                !hook.Request.VisibleFieldPaths.Contains("secret") &&
                !hook.Request.VisibleFieldPaths.Contains("lines.cost"));
            var called = hook.CallCount;
            var denied = await FailsAsync(() => service.GetAsync(new CenterScope { PersonId = "stranger" }, "a-todo"));
            check("无权详情在出站钩子前统一拒绝且不泄露原单", denied == ErrorCodes.NotFound && hook.CallCount == called);
            hook.Response.Url = "https://other.example.com/read";
            check("原单 URL 所属来源必须匹配当前应用", await FailsAsync(() => service.GetAsync(person, "a-todo")) == ErrorCodes.BusinessHookFailed);
            hook.Response.Url = "http://documents.example.com/read";
            check("原单 URL 即使配置允许内网钩子仍必须为 HTTPS", await FailsAsync(() => service.GetAsync(person, "a-todo")) == ErrorCodes.BusinessHookFailed);
            hook.Response.Url = null!;
            check("业务钩子返回 null 地址时按协议错误回退而非服务器异常",
                await FailsAsync(() => service.GetAsync(person, "a-todo")) == ErrorCodes.BusinessHookFailed);
            hook.Response.Url = "https://documents.example.com/read";
            hook.Response.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(6);
            check("原单地址有效期超过五分钟会被拒绝", await FailsAsync(() => service.GetAsync(person, "a-todo")) == ErrorCodes.BusinessHookFailed);
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.ExternalPersonBindings.Add(new ApprovalService.Domain.Organization.ExternalPersonBindingEntity
                {
                    ClientAppCode = "one",
                    ExternalUserId = "duplicate",
                    PersonId = "viewer",
                });
                await db.SaveChangesAsync();
            }
            called = hook.CallCount;
            check("查看人存在多个本应用外部绑定时不签发地址",
                await FailsAsync(() => service.GetAsync(person, "a-todo")) == ErrorCodes.ValidationFailed &&
                hook.CallCount == called);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 捕获用例对调用方可纠正问题的稳定错误码。
    /// </summary>
    /// <param name="run">待检查调用。</param>
    /// <returns>错误码；意外成功则为空。</returns>
    private static async Task<string?> FailsAsync(Func<Task<CenterDocumentUrlResponse>> run)
    {
        try
        {
            await run();
            return null;
        }
        catch (UserFriendlyException error)
        {
            return error.Code;
        }
    }

    /// <summary>
    /// 仅替换已由独立场景覆盖的 HTTP HMAC 传输，保留真实中心查询。
    /// </summary>
    private sealed class DocumentHookStub : IApprovalHookClient
    {
        /// <summary>
        /// 最近一次业务原单请求。
        /// </summary>
        public DocumentHookRequest? Request
        {
            get; private set;
        }

        /// <summary>
        /// 调用次数。
        /// </summary>
        public int CallCount
        {
            get; private set;
        }

        /// <summary>
        /// 可调整的签名响应正文。
        /// </summary>
        public DocumentHookResponse Response
        {
            get;
        } = new()
        {
            Url = "https://documents.example.com/read?once=demo",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
        };

        /// <inheritdoc />
        public Task<JsonElement> InvokeAsync(ClientAppEntity app, string url, object request, string requestId,
            CancellationToken cancellationToken = default)
        {
            Request = request as DocumentHookRequest;
            CallCount++;
            return Task.FromResult(JsonSerializer.SerializeToElement(Response, ApprovalJson.Options));
        }
    }
}
