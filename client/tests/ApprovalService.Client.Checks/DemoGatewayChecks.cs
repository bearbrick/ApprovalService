using System.Net;
using System.Net.Http;
using System.Text;
using ApprovalService.Client;
using ApprovalService.Demo.Integration;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 对接层经真实 SDK 组装发起请求并把查询结果写回独立业务库。
/// </summary>
internal static class DemoGatewayChecks
{
    /// <summary>
    /// 用假 HTTP 服务验证报销和采购数据、稳定请求 ID 以及离线查询同步。
    /// </summary>
    /// <param name="check">检查报告。</param>
    /// <returns>所有示例对接场景完成。</returns>
    internal static async Task RunAsync(Action<string, bool> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "approval-demo-gateway-" + Guid.NewGuid().ToString("N") + ".json");
        var seen = new List<(string Path, string? RequestId, string Body)>();
        try
        {
            var starts = 0;
            var handler = new FakeHandler(async request =>
            {
                var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                var requestId = request.Headers.TryGetValues("X-Request-Id", out var values) ? values.SingleOrDefault() : null;
                seen.Add((request.RequestUri?.AbsolutePath ?? string.Empty, requestId, body));
                if (request.Method == HttpMethod.Post)
                {
                    starts++;
                    return Json(starts == 1 ? Instance("inst-e", "EXPENSE", "EXP-1", "running") :
                        Instance("inst-p", "PURCHASE_REQUEST", "PUR-1", "running"));
                }

                return Json(Instance("inst-e", "EXPENSE", "EXP-1", "approved", "\"verifiedAmount\":800"));
            });
            using var sdk = new ApprovalServiceClient(new ApprovalServiceClientOptions
            {
                BaseAddress = new Uri("https://approval.example.test/"), ApiKey = "demo-key",
            }, handler);
            var store = new DemoDocumentStore(path);
            var gateway = new DemoApprovalGateway(sdk, store);
            var expense = await gateway.SubmitExpenseAsync(new ExpenseClaim
            {
                BusinessKey = "EXP-1", Initiator = "wang", Department = "production",
                Title = "车票", Category = "交通", ClaimAmount = 860m, ReceiptFileId = "receipt-1",
                Lines = new List<ExpenseLine> { new() { Date = "2026-10-24", Description = "高铁", Amount = 860m } },
            }, "expense-start-1").ConfigureAwait(false);
            check("报销对接先保存原单并把快照和稳定请求 ID 发给 SDK", expense.Id == "inst-e"
                && store.FindExpense("EXP-1")?.ApprovalInstanceId == "inst-e"
                && seen[0].RequestId == "expense-start-1" && seen[0].Body.Contains("\"receipt\":\"receipt-1\"", StringComparison.Ordinal)
                && seen[0].Body.Contains("\"businessKey\":\"EXP-1\"", StringComparison.Ordinal));

            var purchase = await gateway.SubmitPurchaseAsync(new PurchaseRequest
            {
                BusinessKey = "PUR-1", Initiator = "zheng", Department = "purchase",
                Title = "采购电源", BaseAmount = 100m, TaxAmount = 13m,
                Items = new List<PurchaseLine> { new() { ItemName = "电源", Quantity = 2m, UnitPrice = 50m } },
            }, "purchase-start-1").ConfigureAwait(false);
            check("采购对接传未税额和税额，由审批服务计算总额", purchase.Id == "inst-p"
                && store.FindPurchase("PUR-1")?.ApprovalInstanceId == "inst-p"
                && seen[1].RequestId == "purchase-start-1" && seen[1].Body.Contains("\"baseAmount\":100", StringComparison.Ordinal)
                && !seen[1].Body.Contains("\"totalAmount\"", StringComparison.Ordinal));

            await gateway.RefreshAsync("inst-e").ConfigureAwait(false);
            check("无本机后端时打开原单查询审批状态并同步核定金额", seen[2].Path == "/api/v1/instances/inst-e"
                && store.FindExpense("EXP-1") is { ApprovalStatus: "approved", VerifiedAmount: 800m });
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// 构造 SDK 所需的完整实例响应。
    /// </summary>
    /// <param name="id">审批单 ID。</param>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="status">审批状态。</param>
    /// <param name="approverValues">审批人字段内容。</param>
    /// <returns>JSON 正文。</returns>
    private static string Instance(string id, string flowCode, string businessKey, string status,
        string approverValues = "") =>
        "{\"id\":\"" + id + "\",\"serialNo\":\"DEMO-1\",\"flowCode\":\"" + flowCode +
        "\",\"businessKey\":\"" + businessKey + "\",\"title\":\"演示审批\",\"initiatorId\":\"person-1\",\"clientAppCode\":\"demo\",\"status\":\"" +
        status + "\",\"data\":{},\"approverFieldValues\":{" + approverValues +
        "},\"startedAt\":\"2026-10-24T08:00:00Z\",\"tasks\":[],\"events\":[]}";

    /// <summary>
    /// 返回 JSON 成功响应。
    /// </summary>
    /// <param name="json">响应正文。</param>
    /// <returns>HTTP 响应。</returns>
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <summary>
    /// 截获 SDK 请求的最小 HTTP 处理器。
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        /// <summary>
        /// 返回响应的回调。
        /// </summary>
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> respond;

        /// <summary>
        /// 创建假处理器。
        /// </summary>
        /// <param name="respond">按请求返回响应。</param>
        internal FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => this.respond = respond;

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
