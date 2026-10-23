using System.Net;
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
/// 用真实 HTTP 与 SQLite 验证发布流程预览不落库、分支与当前候选人。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 复用前一个 PATCH 场景登记的发布流程，按不同数据预览预计路径。
    /// </summary>
    /// <param name="services">数据库服务范围。</param>
    /// <param name="http">进程内 HTTP 客户端。</param>
    /// <param name="apiKey">接入应用 Key。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunPreviewAsync(IServiceProvider services, HttpClient http, string apiKey,
        Action<string, bool> check)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using var beforeDb = await factory.CreateDbContextAsync();
        var before = await beforeDb.Instances.CountAsync();
        var beforeOutbox = await beforeDb.OutboxItems.CountAsync();
        using var highRequest = PreviewRequest(apiKey, "preview-high", "alice", 5);
        using var highResponse = await http.SendAsync(highRequest);
        var high = JsonSerializer.Deserialize<OpenFlowPreviewResponse>(
            await highResponse.Content.ReadAsStringAsync(), ApprovalJson.Options);
        check("预览无需业务单号，按当前金额选预计分支并返回候选审批人",
            highResponse.StatusCode == HttpStatusCode.OK && high is
            {
                FlowCode: "PATCH", ProjectedStatus: EngineInstanceStatuses.Approved
            } &&
            high.Nodes.Any(node => node.NodeId == "high" &&
                node.CandidateAssigneeIds.SequenceEqual(["requester"]) &&
                node.CandidateAssignees is [{ Id: "requester", Name: "发起人" }]) &&
            !high.Nodes.Any(node => node.NodeId == "low") &&
            high.Notice.Contains("实际到达节点时", StringComparison.Ordinal));

        using var lowRequest = PreviewRequest(apiKey, "preview-low", "alice", 2);
        using var lowResponse = await http.SendAsync(lowRequest);
        var low = JsonSerializer.Deserialize<OpenFlowPreviewResponse>(
            await lowResponse.Content.ReadAsStringAsync(), ApprovalJson.Options);
        check("预览以同一发布图和另一份数据重新选择兜底分支",
            lowResponse.StatusCode == HttpStatusCode.OK && low is not null &&
            low.Nodes.Any(node => node.NodeId == "low") &&
            !low.Nodes.Any(node => node.NodeId == "high"));

        using var unknownRequest = PreviewRequest(apiKey, "preview-unbound", "E001", 5);
        using var unknownResponse = await http.SendAsync(unknownRequest);
        check("预览发起人仍须当前应用既有绑定", unknownResponse.StatusCode == HttpStatusCode.BadRequest);

        await using var afterDb = await factory.CreateDbContextAsync();
        check("流程预览不占业务单号、不写审批单和待发回调",
            await afterDb.Instances.CountAsync() == before &&
            await afterDb.OutboxItems.CountAsync() == beforeOutbox);
    }

    /// <summary>
    /// 构造预览请求，故意省略业务单号。
    /// </summary>
    /// <param name="apiKey">接入应用 Key。</param>
    /// <param name="requestId">稳定请求 ID。</param>
    /// <param name="initiator">当前应用外部发起人。</param>
    /// <param name="amount">测试分支金额。</param>
    /// <returns>带凭证的 HTTP 请求。</returns>
    private static HttpRequestMessage PreviewRequest(string apiKey, string requestId, string initiator, int amount)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            OpenApiRoutes.FlowPreview.Replace("{code}", "PATCH"))
        {
            Content = JsonContent.Create(new OpenFlowPreviewRequest
            {
                Initiator = initiator,
                Data = JsonSerializer.SerializeToElement(new
                {
                    amount,
                    locked = "original",
                    lines = new[] { new { qty = 1, code = "a" }, new { qty = 2, code = "b" } },
                }),
            }),
        };
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        request.Headers.Add(HeaderNames.RequestId, requestId);
        return request;
    }
}
