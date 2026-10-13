using ApprovalService.Demo.Integration;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 对真实服务的端到端检查：设置环境变量 APPROVAL_SERVICE_URL 时才跑（根目录 verify.ps1 全量验证时会临时启动服务并设置）。
/// </summary>
internal static class LiveServerChecks
{
    /// <summary>
    /// 运行检查；没设置服务地址时跳过并说明。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    /// <returns>检查做完时完成。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var url = Environment.GetEnvironmentVariable("APPROVAL_SERVICE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.WriteLine("SKIP: 未设置 APPROVAL_SERVICE_URL，跳过对真实服务的端到端检查（运行根目录 verify.ps1 会自动执行）");
            return;
        }

        using var connection = new ApprovalConnection(new Uri(url), apiKey: null);
        var info = await connection.Client.GetInfoAsync();
        check($"端到端：SDK 连上真实服务 {url}，服务名称 ApprovalService、接口 v1", info is { Name: "ApprovalService", ApiVersion: "v1" });
        var description = await connection.DescribeAsync();
        check("端到端：对接类库能描述连接状态", description.StartsWith("已连接审批流服务", StringComparison.Ordinal));

        var missing = false;
        try
        {
            using var wrong = new ApprovalServiceClient(new ApprovalServiceClientOptions { BaseAddress = new Uri(new Uri(url), "no-such-prefix/") });
            await wrong.GetInfoAsync();
        }
        catch (ApprovalServiceException ex) when (ex.ErrorCode == ApprovalErrorCodes.NotFound && !string.IsNullOrEmpty(ex.TraceId))
        {
            missing = true;
        }

        check("端到端：地址不对时得到 NOT_FOUND 和链路 ID", missing);
    }
}
