using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 业务原单作废时终止待补充审批的真实 HTTP 和持久化场景。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 确认等待发起人处理仍可终止，并原子释放在途业务单号。
    /// </summary>
    /// <param name="services">已迁移的检查服务。</param>
    /// <param name="http">真实 HTTP 客户端。</param>
    /// <param name="apiKey">审批单所属应用 API Key。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunAwaitingTerminationAsync(IServiceProvider services, HttpClient http, string apiKey,
        Action<string, bool> check)
    {
        using var startRequest = Request(new OpenStartRequest
        {
            FlowCode = "PURCHASE",
            BusinessKey = "VOID-WHILE-AWAITING",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { amount = 44 }),
        }, apiKey, "awaiting-terminate-start");
        using var startResponse = await http.SendAsync(startRequest);
        var started = await startResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("待补充终止场景先生成在途审批", startResponse.StatusCode == HttpStatusCode.OK &&
            started is { Status: EngineInstanceStatuses.Running });

        using var supplementRequest = ActionRequest(started!.Tasks.Single().Id, new OpenTaskActionRequest
        {
            User = "bob",
            Action = OperationKinds.RequestSupplement,
            Opinion = "请补资料",
        }, apiKey, "awaiting-terminate-supplement");
        using var supplementResponse = await http.SendAsync(supplementRequest);
        var awaiting = await supplementResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("要求补充后审批单仍属于在途", supplementResponse.StatusCode == HttpStatusCode.OK &&
            awaiting is { Status: EngineInstanceStatuses.AwaitingInitiator });

        using var terminateRequest = CommandRequest(OpenApiRoutes.InstanceTerminate.Replace("{id}", started.Id),
            "secretary", apiKey, "awaiting-terminate-action", "原业务单据作废");
        using var terminateResponse = await http.SendAsync(terminateRequest);
        var terminated = await terminateResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("业务单据作废时已绑定操作人可终止待补充审批",
            terminateResponse.StatusCode == HttpStatusCode.OK &&
            terminated is { Status: EngineInstanceStatuses.Terminated } &&
            terminated.Events.Any(item => item.Event == "instanceTerminate"));

        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        check("终止待补充审批在同一事务释放业务单号占用",
            !await db.ActiveInstanceKeys.AsNoTracking().AnyAsync(item => item.InstanceId == started.Id));
    }
}
