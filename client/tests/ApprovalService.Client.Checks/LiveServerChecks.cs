using System.Text.Json;
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

        var fixtureFile = Environment.GetEnvironmentVariable("APPROVAL_TEST_FIXTURE_PATH");
        if (string.IsNullOrWhiteSpace(fixtureFile))
        {
            Console.WriteLine("SKIP: 未设置 APPROVAL_TEST_FIXTURE_PATH，跳过需测试接入应用的成功链路");
            return;
        }

        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixtureFile));
        var apiKey = fixture.RootElement.GetProperty("apiKey").GetString()
            ?? throw new InvalidOperationException("临时测试夹具缺少 API Key。");
        var flowCode = fixture.RootElement.GetProperty("flowCode").GetString()
            ?? throw new InvalidOperationException("临时测试夹具缺少流程编码。");
        var initiator = fixture.RootElement.GetProperty("initiator").GetString()
            ?? throw new InvalidOperationException("临时测试夹具缺少发起人。");
        var approver = fixture.RootElement.GetProperty("approver").GetString()
            ?? throw new InvalidOperationException("临时测试夹具缺少审批人。");
        using var client = new ApprovalServiceClient(new ApprovalServiceClientOptions
        {
            BaseAddress = new Uri(url),
            ApiKey = apiKey,
        });
        var flows = await client.GetFlowsAsync();
        var schema = await client.GetFlowSchemaAsync(flowCode);
        check("端到端：随机 API Key 只能读取已授权发布流程与结构",
            flows.Any(flow => flow.Code == flowCode && flow.Version == 1) &&
            schema.FlowCode == flowCode && schema.Version == 1);

        var businessKey = "SDK-" + Guid.NewGuid().ToString("N");
        var started = await client.StartAsync(new StartApprovalRequest
        {
            FlowCode = flowCode,
            BusinessKey = businessKey,
            Title = "SDK 端到端验证",
            Initiator = initiator,
            Data = new Dictionary<string, object?>(),
        });
        var detail = await client.GetInstanceAsync(started.Id);
        var history = await client.FindInstancesAsync(flowCode, businessKey);
        check("端到端：SDK 发起后可按 ID 与业务单号读取同一在途审批",
            started.Status == "running" && started.ClientAppCode == "sdk-e2e" &&
            detail.Id == started.Id && detail.Tasks.Count == 1 &&
            history.Count == 1 && history[0].Id == started.Id);

        var count = await client.GetTaskCountAsync(approver);
        var tasks = await client.GetTasksAsync(approver);
        check("端到端：绑定审批人能通过 SDK 看到待办数量和完整任务 ID",
            count.Pending == 1 && tasks.Count == 1 && tasks[0].InstanceId == started.Id &&
            tasks[0].Id == detail.Tasks[0].Id);

        var approved = await client.ActOnTaskAsync(tasks[0].Id, new ApprovalTaskActionRequest
        {
            User = approver,
            Action = "approve",
            Opinion = "同意",
        });
        var completed = await client.GetInstanceAsync(started.Id);
        var after = await client.GetTaskCountAsync(approver);
        check("端到端：SDK 审批后实例与待办投影同步完成",
            approved.Status == "approved" && completed.Status == "approved" &&
            completed.Tasks.Single().Status == "approved" && after.Pending == 0);

        var second = await client.StartAsync(new StartApprovalRequest
        {
            FlowCode = flowCode,
            BusinessKey = businessKey + "-withdraw",
            Title = "SDK 催办撤回验证",
            Initiator = initiator,
            Data = new Dictionary<string, object?>(),
        });
        var reminded = await client.RemindAsync(second.Id, initiator);
        var withdrawn = await client.WithdrawAsync(second.Id, initiator);
        check("端到端：SDK 催办后发起人可撤回未决审批",
            reminded.Status == "running" && withdrawn.Status == "withdrawn" &&
            (await client.GetTaskCountAsync(approver)).Pending == 0);
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
