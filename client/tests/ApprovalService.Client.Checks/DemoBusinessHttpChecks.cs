using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ApprovalService.Demo.Integration;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 本机业务后端的双向签名、预算校验、取人和动作重试场景。
/// </summary>
internal static class DemoBusinessHttpChecks
{
    /// <summary>
    /// 经真实回环 HTTP 请求验证业务钩子和回调。
    /// </summary>
    /// <param name="check">检查报告。</param>
    /// <returns>全部本机 HTTP 场景结束。</returns>
    internal static async Task RunAsync(Action<string, bool> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "approval-demo-http-" + Guid.NewGuid().ToString("N") + ".json");
        const string secret = "only-for-memory-check";
        try
        {
            var store = new DemoDocumentStore(path);
            store.SaveExpense(new ExpenseClaim
            {
                BusinessKey = "EXP-HOOK",
                Initiator = "wang",
                Department = "production",
                Title = "车票",
                Category = "交通",
                ClaimAmount = 860m,
                ReceiptFileId = "file-hook",
                Lines = new List<ExpenseLine> { new() { Date = "2026-10-24", Description = "高铁", Amount = 860m } },
            });
            store.SavePurchase(new PurchaseRequest
            {
                BusinessKey = "PUR-HOOK",
                Initiator = "zheng",
                Department = "purchase",
                Title = "采购电源",
                BaseAmount = 100m,
                TaxAmount = 13m,
                WithinBudget = true,
                ProjectCode = "P1",
            });
            var port = FreePort();
            var prefix = "http://127.0.0.1:" + port + "/";
            using var host = new DemoBusinessHttpHost(prefix, secret, store,
                new Dictionary<string, string> { ["P1"] = "li-na" }, 100m, actionFailures: 2);
            host.Start();
            using var http = new HttpClient { BaseAddress = new Uri(prefix), Timeout = TimeSpan.FromSeconds(5) };

            var data = await SendAsync(http, "data",
                "{\"flowCode\":\"EXPENSE\",\"businessKey\":\"EXP-HOOK\",\"requestId\":\"demo-data-1\"}",
                "demo-data-1", secret).ConfigureAwait(false);
            check("取数钩子返回真实报销原单且响应正文和请求 ID 双向验签", data.Status == 200
                && data.RequestId == "demo-data-1" && data.Signature == Sign(data.Body, secret)
                && Encoding.UTF8.GetString(data.Body).Contains("\"receipt\":\"file-hook\"", StringComparison.Ordinal));

            var tampered = await SendAsync(http, "data",
                "{\"flowCode\":\"EXPENSE\",\"businessKey\":\"EXP-HOOK\",\"requestId\":\"demo-data-2\"}",
                "demo-data-2", secret, signatureOverride: "sha256=" + new string('0', 64)).ConfigureAwait(false);
            check("篡改请求正文或签名时本机钩子拒绝", tampered.Status == 401);

            var validation = await SendAsync(http, "validate",
                "{\"phase\":\"start\",\"flowCode\":\"PURCHASE_REQUEST\",\"businessKey\":\"PUR-HOOK\"}",
                "demo-valid-1", secret).ConfigureAwait(false);
            check("预算内采购超余额时返回可理解的差额", validation.Status == 200
                && Encoding.UTF8.GetString(validation.Body).Contains("\"allowed\":false", StringComparison.Ordinal)
                && Encoding.UTF8.GetString(validation.Body).Contains("13.00", StringComparison.Ordinal));

            var assignees = await SendAsync(http, "assignees",
                "{\"flowCode\":\"PURCHASE_REQUEST\",\"businessKey\":\"PUR-HOOK\",\"nodeId\":\"project\"}",
                "demo-assignee-1", secret).ConfigureAwait(false);
            check("取人钩子按本机项目映射返回待绑定业务用户", assignees.Status == 200
                && Encoding.UTF8.GetString(assignees.Body).Contains("li-na", StringComparison.Ordinal));

            const string actionBody = "{\"eventId\":\"action-1\",\"actionCode\":\"createPurchaseOrder\",\"flowCode\":\"PURCHASE_REQUEST\",\"businessKey\":\"PUR-HOOK\"}";
            var first = await SendAsync(http, "action", actionBody, "demo-action-1", secret).ConfigureAwait(false);
            var second = await SendAsync(http, "action", actionBody, "demo-action-1", secret).ConfigureAwait(false);
            var third = await SendAsync(http, "action", actionBody, "demo-action-1", secret).ConfigureAwait(false);
            var duplicate = await SendAsync(http, "action", actionBody, "demo-action-1", secret).ConfigureAwait(false);
            check("自动采购动作前两次失败、第三次成功且同事件重发复用订单号",
                Encoding.UTF8.GetString(first.Body).Contains("\"success\":false", StringComparison.Ordinal)
                && Encoding.UTF8.GetString(second.Body).Contains("\"success\":false", StringComparison.Ordinal)
                && Encoding.UTF8.GetString(third.Body).Contains("PO-PUR-HOOK", StringComparison.Ordinal)
                && third.Body.SequenceEqual(duplicate.Body));

            var malformed = await SendAsync(http, "callback", "{\"eventId\":\"missing-fields\"}",
                "demo-callback-bad", secret).ConfigureAwait(false);
            check("已签名但缺字段的回调返回可修正错误且不停止本机服务", malformed.Status == 400);

            const string callback = "{\"eventId\":\"callback-1\",\"event\":\"instance.approved\",\"instanceId\":\"inst-p\",\"serialNo\":\"DEMO-1\",\"flowCode\":\"PURCHASE_REQUEST\",\"businessKey\":\"PUR-HOOK\",\"status\":\"approved\",\"data\":{},\"actionResults\":{\"action-1\":{\"orderNo\":\"PO-PUR-HOOK\"}},\"occurredAt\":\"2026-10-24T08:00:00Z\"}";
            var callbackResponse = await SendAsync(http, "callback", callback, "demo-callback-1", secret).ConfigureAwait(false);
            check("原文验签回调将订单号和审批状态写回业务库", callbackResponse.Status == 200
                && store.FindPurchase("PUR-HOOK") is { OrderNumber: "PO-PUR-HOOK", ApprovalStatus: "approved" });
            var repeated = await SendAsync(http, "callback", callback, "demo-callback-1", secret).ConfigureAwait(false);
            check("回调重发按事件 ID 持久去重并仍返回成功", repeated.Status == 200
                && new DemoDocumentStore(path).FindPurchase("PUR-HOOK")?.ApprovalStatus == "approved");
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
    /// 占用再释放一个本机端口供 HttpListener 测试使用。
    /// </summary>
    /// <returns>可尝试绑定的端口。</returns>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// 发送带原文 HMAC 与请求 ID 的钩子调用。
    /// </summary>
    /// <param name="http">回环 HTTP 客户端。</param>
    /// <param name="path">固定钩子路径。</param>
    /// <param name="json">原始 JSON 正文。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="secret">测试签名密钥。</param>
    /// <param name="signatureOverride">用于验证篡改拒绝的可选签名。</param>
    /// <returns>状态、正文及响应验签材料。</returns>
    private static async Task<(int Status, byte[] Body, string? Signature, string? RequestId)> SendAsync(
        HttpClient http, string path, string json, string requestId, string secret, string? signatureOverride = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        var bytes = Encoding.UTF8.GetBytes(json);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("X-Request-Id", requestId);
        request.Headers.TryAddWithoutValidation("X-Approval-Signature", signatureOverride ?? Sign(bytes, secret));
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        return ((int)response.StatusCode, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false),
            response.Headers.TryGetValues("X-Approval-Signature", out var signatures) ? signatures.SingleOrDefault() : null,
            response.Headers.TryGetValues("X-Request-Id", out var ids) ? ids.SingleOrDefault() : null);
    }

    /// <summary>
    /// 测试端按公开协议生成签名，独立核对业务后端实现。
    /// </summary>
    /// <param name="body">原始字节。</param>
    /// <param name="secret">签名密钥。</param>
    /// <returns>签名头。</returns>
    private static string Sign(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(body);
        return "sha256=" + BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
    }
}
