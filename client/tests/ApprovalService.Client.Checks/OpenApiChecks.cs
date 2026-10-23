using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 以假 HTTP 服务逐项检查首批开放接口的路径、正文、响应模型和错误。
/// </summary>
internal static class OpenApiChecks
{
    /// <summary>
    /// 审批单详情的完整最小响应。
    /// </summary>
    private const string InstanceJson = """
        {"id":"instance-1","serialNo":"SP-001","flowCode":"FLOW_A","businessKey":"ORDER/1","title":"测试审批", "initiatorId":"person-1","initiatorDepartmentId":null,"submittedById":null,"clientAppCode":"app-a","status":"running","data":{"amount":42},"startedAt":"2026-10-20T08:00:00Z","endedAt":null,"tasks":[{"id":"instance-1/approval/1","nodeId":"approval","assigneeId":"person-2","status":"pending","opinion":null,"dueAt":null,"completedAt":null}],"events":[{"eventId":"event-1","sequence":1,"event":"started","detail":{},"occurredAt":"2026-10-20T08:00:00Z"}]}
        """;

    /// <summary>
    /// 运行所有开放接口场景。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    /// <returns>检查结束时完成。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var seen = new List<SeenRequest>();
        using var handler = new FakeHandler(async request =>
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            seen.Add(new SeenRequest(request.Method.Method, request.RequestUri?.PathAndQuery ?? "", body,
                request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.SingleOrDefault() : null));
            var path = request.RequestUri?.AbsolutePath ?? "";
            var method = request.Method.Method;
            if (path.EndsWith("/api/v1/flows", StringComparison.Ordinal))
            {
                return Json("""[{"code":"FLOW_A","name":"测试流程","version":2}]""");
            }

            if (path.EndsWith("/schema", StringComparison.Ordinal))
            {
                return Json("""{"flowCode":"FLOW_A","version":2,"data":{"fields":[]},"example":{"amount":0}}""");
            }

            if (path.EndsWith("/api/v1/instances", StringComparison.Ordinal) && method == "GET")
            {
                return Json("[" + InstanceJson + "]");
            }

            if (path.EndsWith("/api/v1/tasks/count", StringComparison.Ordinal))
            {
                return Json("""{"pending":1,"urgent":1,"overdue":0}""");
            }

            if (path.EndsWith("/api/v1/tasks", StringComparison.Ordinal))
            {
                return Json("""[{"id":"instance-1/approval/1","instanceId":"instance-1","flowCode":"FLOW_A","businessKey":"ORDER/1","title":"测试审批","nodeId":"approval","isUrgent":true,"dueAt":null,"summary":{"amount":42},"openPath":"/#/approval/instance-1"}]""");
            }

            return Json(InstanceJson);
        });
        using var client = new ApprovalServiceClient(new ApprovalServiceClientOptions
        {
            BaseAddress = new Uri("http://approval.test/prefix/"),
            ApiKey = "test-key",
        }, handler);

        var flows = await client.GetFlowsAsync();
        check("流程列表方法读取编码、名称和发布版本", flows.Count == 1 && flows[0] is { Code: "FLOW_A", Name: "测试流程", Version: 2 }
            && Seen(seen, "GET", "/prefix/api/v1/flows"));

        var schema = await client.GetFlowSchemaAsync("FLOW/A");
        check("数据定义方法编码路径段并保留 JSON 样例", schema.FlowCode == "FLOW_A" && schema.Example.ContainsKey("amount")
            && Seen(seen, "GET", "/prefix/api/v1/flows/FLOW%2FA/schema"));

        var started = await client.StartAsync(new StartApprovalRequest
        {
            FlowCode = "FLOW_A",
            BusinessKey = "ORDER/1",
            Title = "测试审批",
            Initiator = "staff-1",
            Data = new Dictionary<string, object?> { ["amount"] = 42 },
            IsUrgent = true,
            SelectedAssignees = new Dictionary<string, List<string>> { ["approval"] = new() { "staff-2" } },
        });
        var start = seen.Last();
        check("发起方法用 POST JSON 并保留业务数据和自选人员", started.Id == "instance-1"
            && start is { Method: "POST", Path: "/prefix/api/v1/instances", ApiKey: "test-key" }
            && JsonField(start.Body, "businessKey") == "ORDER/1" && JsonField(start.Body, "initiator") == "staff-1"
            && JsonField(start.Body, "data", "amount") == "42" && JsonField(start.Body, "selectedAssignees", "approval", 0) == "staff-2");

        var instance = await client.GetInstanceAsync("instance/1");
        check("按 ID 查询审批单时编码路径并解析任务、事件和时间", instance.Tasks.Count == 1
            && instance.Events.Count == 1 && instance.StartedAt.Year == 2026 && instance.Data.ContainsKey("amount")
            && Seen(seen, "GET", "/prefix/api/v1/instances/instance%2F1"));

        var history = await client.FindInstancesAsync("FLOW/A", "ORDER/1");
        check("按流程和单号查询历史时两个查询参数均编码", history.Count == 1 && history[0].BusinessKey == "ORDER/1"
            && Seen(seen, "GET", "/prefix/api/v1/instances?flowCode=FLOW%2FA&businessKey=ORDER%2F1"));

        var count = await client.GetTaskCountAsync("staff/1", allApps: true);
        check("待办数量方法映射三种数量并传跨应用范围", count is { Pending: 1, Urgent: 1, Overdue: 0 }
            && Seen(seen, "GET", "/prefix/api/v1/tasks/count?user=staff%2F1&scope=all"));

        var tasks = await client.GetTasksAsync("staff/1", skip: 3, take: 10);
        check("待办列表方法传分页并解析摘要与打开路径", tasks.Count == 1 && tasks[0].Summary.ContainsKey("amount")
            && tasks[0].OpenPath.Contains("instance-1", StringComparison.Ordinal)
            && Seen(seen, "GET", "/prefix/api/v1/tasks?user=staff%2F1&skip=3&take=10"));

        var acted = await client.ActOnTaskAsync("instance-1/approval/1", new ApprovalTaskActionRequest
        {
            User = "staff-2",
            Action = "approve",
            Opinion = "同意",
            MentionedUsers = new List<string> { "staff-3" },
            FieldValues = new Dictionary<string, object?> { ["approvalNote"] = "已核对", ["score"] = 5 },
        });
        var action = seen.Last();
        check("任务操作编码含斜杠 ID 并传操作人、动作和意见", acted.Id == "instance-1"
            && action.Method == "POST" && action.Path.EndsWith("/tasks/instance-1%2Fapproval%2F1/actions", StringComparison.Ordinal)
            && JsonField(action.Body, "action") == "approve" && JsonField(action.Body, "opinion") == "同意"
            && JsonField(action.Body, "fieldValues", "approvalNote") == "已核对"
            && JsonField(action.Body, "fieldValues", "score") == "5");

        var withdrawn = await client.WithdrawAsync("instance/1", "staff-1");
        var withdraw = seen.Last();
        check("撤回方法传发起人身份并解析审批单", withdrawn.Id == "instance-1"
            && withdraw.Path.EndsWith("/instances/instance%2F1/withdraw", StringComparison.Ordinal)
            && JsonField(withdraw.Body, "user") == "staff-1");

        var reminded = await client.RemindAsync("instance/1", "staff-1");
        var remind = seen.Last();
        check("催办方法传发起人身份并解析审批单", reminded.Id == "instance-1"
            && remind.Path.EndsWith("/instances/instance%2F1/remind", StringComparison.Ordinal)
            && JsonField(remind.Body, "user") == "staff-1");

        using var conflict = new FakeHandler(_ => Task.FromResult(Json("""{"code":"CONFLICT","message":"审批单已结束。","traceId":"trace-2"}""", HttpStatusCode.Conflict)));
        using var conflictClient = new ApprovalServiceClient(new ApprovalServiceClientOptions { BaseAddress = new Uri("http://approval.test/") }, conflict);
        ApprovalServiceException? error = null;
        try
        {
            await conflictClient.WithdrawAsync("instance-1", "staff-1");
        }
        catch (ApprovalServiceException ex)
        {
            error = ex;
        }

        check("开放接口业务错误保留服务端错误码、状态与链路 ID", error is { ErrorCode: ApprovalErrorCodes.Conflict, StatusCode: 409, TraceId: "trace-2" });
    }

    /// <summary>
    /// 判断指定方法与相对路径是否被调用。
    /// </summary>
    /// <param name="seen">已捕获的请求。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">路径与查询。</param>
    /// <returns>是否存在。</returns>
    private static bool Seen(IEnumerable<SeenRequest> seen, string method, string path) =>
        seen.Any(request => request.Method == method && request.Path == path);

    /// <summary>
    /// 从测试请求正文读取对象字段或数组元素。
    /// </summary>
    /// <param name="body">JSON 正文。</param>
    /// <param name="names">依次访问的对象名或数组下标。</param>
    /// <returns>最后一层的文本。</returns>
    private static string? JsonField(string? body, params object[] names)
    {
        if (body == null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        var value = document.RootElement;
        foreach (var name in names)
        {
            value = name is int index ? value[index] : value.GetProperty((string)name);
        }

        return value.ToString();
    }

    /// <summary>
    /// 构造 JSON 响应。
    /// </summary>
    /// <param name="body">JSON 正文。</param>
    /// <param name="status">HTTP 状态。</param>
    /// <returns>HTTP 响应。</returns>
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// 已捕获的请求数据，避免请求对象释放后再读取正文。
    /// </summary>
    /// <param name="Method">HTTP 方法。</param>
    /// <param name="Path">相对路径与查询。</param>
    /// <param name="Body">请求正文。</param>
    /// <param name="ApiKey">API Key。</param>
    private sealed record SeenRequest(string Method, string Path, string? Body, string? ApiKey);

    /// <summary>
    /// 不发网络的 HTTP 处理器。
    /// </summary>
    /// <param name="respond">按请求返回响应。</param>
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
