using System.Security.Cryptography;
using System.Text;

namespace ApprovalService.Client.Checks;

/// <summary>
/// SDK 对独立接入测试回调的验签与兼容解析检查。
/// </summary>
internal static class TestCallbackChecks
{
    /// <summary>
    /// 测试带可选应用编码的合成回调不被业务审批字段误判。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var json = """{"eventId":"test-123","event":"integration.test","instanceId":"test-123","serialNo":"","flowCode":"","businessKey":"","clientAppCode":"erp","status":"test","data":{},"occurredAt":"2026-10-25T00:00:00Z"}""";
        var body = Encoding.UTF8.GetBytes(json);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-secret"));
        var signature = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(body));
        var result = ApprovalCallbackVerifier.VerifyAndParse(body, signature, "test-secret");
        check("SDK 验签后识别不含业务数据的接入测试事件",
            result.EventId == "test-123" && result.Event == ApprovalCallbackEventCodes.IntegrationTest &&
            result.ClientAppCode == "erp" && result.BusinessKey.Length == 0 && result.Data.Count == 0 &&
            result.Status == "test");
    }
}
