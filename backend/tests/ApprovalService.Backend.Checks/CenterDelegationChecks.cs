using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用真实 SQLite、JWT 与引擎验证委托规则及已有待办的逐项转移。
/// </summary>
internal static class CenterDelegationChecks
{
    /// <summary>
    /// 验证本人/管理员授权、时间范围、防重叠、真实任务转移和删除语义。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">结果记录器。</param>
    /// <returns>检查完成。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            await using var server = new ServerFactory(backendRoot, directory,
                new Dictionary<string, string?>
                {
                    ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
                });
            using var http = server.CreateClient();
            var tokens = await CenterChecksFixture.SeedAsync(server.Services);
            var now = DateTimeOffset.UtcNow;
            var input = new CenterDelegationRequest
            {
                DelegatePersonId = "first",
                StartsAtUtc = now.AddMinutes(-5),
                EndsAtUtc = now.AddHours(1),
                FlowCodes = ["CENTER"],
            };
            using var anonymous = await http.GetAsync(CenterRoutes.Delegations);
            using var embedded = await SendAsync(http, HttpMethod.Post, CenterRoutes.Delegations, tokens.Embed, input);
            check("委托设置拒绝匿名与嵌入会话写入",
                anonymous.StatusCode == HttpStatusCode.Unauthorized && embedded.StatusCode == HttpStatusCode.Forbidden);

            using var createdResponse = await SendAsync(http, HttpMethod.Post, CenterRoutes.Delegations,
                tokens.Local, input);
            var created = await createdResponse.Content.ReadFromJsonAsync<CenterDelegationResponse>();
            check("本人可设置限定流程和 UTC 半开时段的委托",
                createdResponse.StatusCode == HttpStatusCode.OK && created?.PrincipalPersonId == "viewer" &&
                created.DelegatePersonId == "first" && created.FlowCodes.SequenceEqual(["CENTER"]));
            if (created is null)
            {
                return;
            }

            using var conflict = await SendAsync(http, HttpMethod.Post, CenterRoutes.Delegations,
                tokens.Local, input);
            using var foreignList = await SendAsync(http, HttpMethod.Get, CenterRoutes.Delegations,
                tokens.Stranger);
            var foreign = await foreignList.Content.ReadFromJsonAsync<List<CenterDelegationResponse>>();
            check("重叠范围由数据库事务拒绝且本人规则不泄露给其他人员",
                conflict.StatusCode == HttpStatusCode.Conflict && foreignList.StatusCode == HttpStatusCode.OK &&
                foreign is { Count: 0 });

            using var ownerTransfer = await SendAsync(http, HttpMethod.Post, CenterRoutes.DelegateTasks,
                tokens.Local, new CenterDelegateTasksRequest { TaskIds = ["a-todo-task", "missing-task"] });
            var ownerResults = await ownerTransfer.Content.ReadFromJsonAsync<List<CenterDelegateTaskResult>>();
            check("本人选择已有任务时逐项独立返回成功与失败",
                ownerTransfer.StatusCode == HttpStatusCode.OK && ownerResults is { Count: 2 } &&
                ownerResults[0].Succeeded && !ownerResults[1].Succeeded && ownerResults[1].Code == ErrorCodes.NotFound);

            using var missingReason = await SendAsync(http, HttpMethod.Post,
                CenterRoutes.AdminDelegateTasks.Replace("{principalId}", "viewer", StringComparison.Ordinal),
                tokens.Stranger, new CenterDelegateTasksRequest { TaskIds = ["a-second"] });
            using var adminTransfer = await SendAsync(http, HttpMethod.Post,
                CenterRoutes.AdminDelegateTasks.Replace("{principalId}", "viewer", StringComparison.Ordinal),
                tokens.Stranger, new CenterDelegateTasksRequest
                {
                    TaskIds = ["a-second"],
                    Reason = "出差期间交给代理人",
                });
            var adminResults = await adminTransfer.Content.ReadFromJsonAsync<List<CenterDelegateTaskResult>>();
            check("管理员转委托必须说明原因且成功时逐项返回",
                missingReason.StatusCode == HttpStatusCode.BadRequest &&
                adminTransfer.StatusCode == HttpStatusCode.OK && adminResults is [{ Succeeded: true }]);

            using (var scope = server.Services.CreateScope())
            {
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await factory.CreateDbContextAsync();
                var moved = await db.ApprovalTasks.Where(item => item.InstanceId == "a-todo")
                    .OrderBy(item => item.Id).ToArrayAsync();
                var events = await db.OperationEvents.Where(item => item.InstanceId == "a-todo" &&
                    item.Kind == "existingTaskDelegated").OrderBy(item => item.Sequence).ToArrayAsync();
                var audit = events.Select(item => JsonSerializer.Deserialize<EngineEvent>(item.PayloadJson,
                    ApprovalJson.Options)?.Audit).ToArray();
                check("已有两项任务委托后代理人和原审批人投影正确，管理员真实身份留痕",
                    moved.Length == 2 && moved.All(item => item.AssigneeId == "first" &&
                        item.DelegatedForId == "viewer") && audit.Length == 2 &&
                    audit[0]?.ActorId == "viewer" && audit[1]?.ActorId == "stranger" &&
                    audit[1]?.Reason == "出差期间交给代理人");
            }

            using var removed = await SendAsync(http, HttpMethod.Delete,
                CenterRoutes.Delegations + "/" + created.Id, tokens.Local);
            using var listAfterDelete = await SendAsync(http, HttpMethod.Get, CenterRoutes.Delegations,
                tokens.Local);
            var current = await listAfterDelete.Content.ReadFromJsonAsync<List<CenterDelegationResponse>>();
            check("删除规则只清除未来委托配置，不反向撤销已有任务",
                removed.StatusCode == HttpStatusCode.NoContent && current is { Count: 0 });
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 只在当前请求头发送人员令牌。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">接口路径。</param>
    /// <param name="token">访问令牌。</param>
    /// <param name="input">可选请求体。</param>
    /// <returns>服务端响应，由调用方释放。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path,
        string token, object? input = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (input is not null)
        {
            request.Content = JsonContent.Create(input);
        }
        return await http.SendAsync(request);
    }
}
