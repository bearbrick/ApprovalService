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

        using var openClient = new ApprovalServiceClient(new ApprovalServiceClientOptions { BaseAddress = new Uri(url) });
        var flowsUnauthorized = await IsUnauthorizedAsync(() => openClient.GetFlowsAsync());
        var startUnauthorized = await IsUnauthorizedAsync(() => openClient.StartAsync(new StartApprovalRequest
        {
            FlowCode = "FLOW_A",
            BusinessKey = "ORDER_A",
            Title = "验证鉴权",
            Initiator = "staff-a",
            Data = new Dictionary<string, object?>(),
        }));
        var tasksUnauthorized = await IsUnauthorizedAsync(() => openClient.GetTaskCountAsync("staff-a"));
        check("端到端：真实开放接口对流程、发起和待办均要求 API Key",
            flowsUnauthorized && startUnauthorized && tasksUnauthorized);
    }

    /// <summary>
    /// 检查真实 HTTP 调用在无 API Key 时被统一鉴权边界拒绝。
    /// </summary>
    /// <param name="call">要调用的 SDK 方法。</param>
    /// <returns>收到带链路 ID 的未授权异常时为 true。</returns>
    private static async Task<bool> IsUnauthorizedAsync(Func<Task> call)
    {
        try
        {
            await call();
            return false;
        }
        catch (ApprovalServiceException ex)
        {
            return ex.ErrorCode == ApprovalErrorCodes.Unauthorized && !string.IsNullOrEmpty(ex.TraceId);
        }
    }
}
