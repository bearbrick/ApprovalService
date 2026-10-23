using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 用假 HTTP 服务检查新增 SDK 方法、附件权限头与回调验签。
/// </summary>
internal static class AdvancedApiChecks
{
    /// <summary>
    /// 可解析的审批单最小快照。
    /// </summary>
    private const string InstanceJson = """
        {"id":"instance-1","serialNo":"SP-001","flowCode":"FLOW_A","businessKey":"ORDER-1","title":"审批","initiatorId":"person-1","initiatorDepartmentId":null,"submittedById":null,"clientAppCode":"app-a","status":"running","data":{},"approverFieldValues":{"decision":"yes"},"startedAt":"2026-10-20T08:00:00Z","endedAt":null,"tasks":[],"events":[]}
        """;

    /// <summary>
    /// 运行新增方法的请求和响应检查。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    /// <returns>全部检查完成。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        string? method = null;
        string? path = null;
        string? body = null;
        string? apiKey = null;
        string? bearer = null;
        string? requestId = null;
        var authentication = new List<(string? Path, string? ApiKey, string? Bearer)>();
        using var handler = new FakeHandler(async request =>
        {
            method = request.Method.Method;
            path = request.RequestUri?.PathAndQuery;
            body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            apiKey = request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.SingleOrDefault() : null;
            bearer = request.Headers.Authorization?.Parameter;
            authentication.Add((path, apiKey, bearer));
            requestId = request.Headers.TryGetValues("X-Request-Id", out var ids) ? ids.SingleOrDefault() : null;
            if (path == "/objects/receipt")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
                };
            }

            if (path != null && path.Contains("/files/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://objects.test/objects/receipt") },
                };
            }

            if (path != null && path.Contains("/flows/", StringComparison.Ordinal))
            {
                return Json("""{"flowCode":"FLOW_A","version":1,"projectedStatus":"approved","notice":"按最新组织重算","nodes":[{"nodeId":"n1","name":"主管","type":"approval","candidateAssigneeIds":["p1"],"candidateAssignees":[{"id":"p1","name":"张三"}]}]}""");
            }

            if (path != null && path.EndsWith("/instances/batch", StringComparison.Ordinal))
            {
                return Json("""{"results":[{"index":0,"instance":INST,"error":null},{"index":1,"instance":null,"error":{"code":"VALIDATION_FAILED","message":"数据无效"}}]}"""
                    .Replace("INST", InstanceJson, StringComparison.Ordinal));
            }

            if (path != null && path.EndsWith("/embed-tokens", StringComparison.Ordinal))
            {
                return Json("""{"token":"one-use","expiresAt":"2026-10-20T08:05:00Z","targetPath":"/#/m/tasks"}""");
            }

            if (path != null && path.EndsWith("/org/changes", StringComparison.Ordinal))
            {
                return Json("""{"applied":true,"issues":[],"departedPersonIds":["p2"]}""");
            }

            if (path != null && path.EndsWith("/org/bindings", StringComparison.Ordinal))
            {
                return Json("""{"personId":"p1"}""");
            }

            if (path != null && path.Contains("/files?", StringComparison.Ordinal))
            {
                return Json("""{"id":"f1","fileName":"票据.png","size":3,"contentType":"image/png","sha256":"abc"}""");
            }

            return Json(InstanceJson);
        });
        using var client = new ApprovalServiceClient(new ApprovalServiceClientOptions
        {
            BaseAddress = new Uri("https://approval.test/prefix/"),
            ApiKey = "app-key",
        }, handler);

        var preview = await client.PreviewAsync("FLOW/A", new ApprovalPreviewRequest
        {
            Initiator = "staff-1",
            Data = new Dictionary<string, object?> { ["amount"] = 12 },
        });
        check("预览方法编码流程路径并解析当前候选人员", path == "/prefix/api/v1/flows/FLOW%2FA/preview"
            && preview.Nodes.Single().CandidateAssignees.Single().Name == "张三" && body!.Contains("\"amount\":12"));

        var batch = await client.StartBatchAsync(new StartApprovalRequest?[]
        {
            new() { FlowCode = "FLOW_A", BusinessKey = "ORDER-1", Title = "审批", Initiator = "staff-1" }, null,
        }, "stable-1");
        check("批量方法保留稳定请求 ID 和逐项成功错误", method == "POST" && requestId == "stable-1"
            && batch[0].Instance?.ApproverFieldValues?["decision"] as string == "yes"
            && batch[1].Error?.Code == ApprovalErrorCodes.ValidationFailed && body!.Contains("null"));

        var patched = await client.PatchDataAsync("instance/1", new ApprovalDataPatchRequest
        {
            User = "staff-1",
            Data = new Dictionary<string, object?> { ["amount"] = 15 },
        });
        check("审批数据 PATCH 发送真实路径、操作人和局部字段", method == "PATCH"
            && path == "/prefix/api/v1/instances/instance%2F1/data" && body!.Contains("\"amount\":15")
            && patched.Id == "instance-1");

        await client.ResubmitAsync("instance-1", new ApprovalResubmitRequest { User = "staff-1" });
        check("重新提交使用实例命令路径", method == "POST"
            && path == "/prefix/api/v1/instances/instance-1/resubmit" && body!.Contains("\"user\":\"staff-1\""));

        await client.TerminateAsync("instance-1", "staff-1", "单据作废");
        check("终止命令提交操作人和原因", path == "/prefix/api/v1/instances/instance-1/terminate"
            && body!.Contains("单据作废"));

        var embed = await client.CreateEmbedTokenAsync("staff-1", "/#/m/tasks");
        check("嵌入令牌方法解析一次性令牌和到期时间", path == "/prefix/api/v1/embed-tokens"
            && embed.Token == "one-use" && embed.ExpiresAt.Year == 2026);

        var changed = await client.PushOrganizationChangesAsync(new OrganizationChangesRequest
        {
            People = new List<OrganizationPerson> { new() { Id = "p1", EmployeeNumber = "001", Name = "张三" } },
        });
        check("组织变更发送人员并解析离职交接名单", path == "/prefix/api/v1/org/changes"
            && body!.Contains("\"employeeNumber\":\"001\"") && changed.DepartedPersonIds.Single() == "p2");

        var bound = await client.BindOrganizationIdAsync(new OrganizationBinding
        {
            Kind = "person",
            ExternalId = "staff-1",
            EmployeeNumber = "001",
        });
        check("外部人员绑定不接受直接指定内部人员 ID", path == "/prefix/api/v1/org/bindings"
            && bound == "p1" && body!.Contains("\"method\":\"push\""));

        using var file = new MemoryStream(new byte[] { 1, 2, 3 });
        var uploaded = await client.UploadFileAsync("票据.png", file);
        check("附件上传发送原始字节和已编码文件名", path == "/prefix/api/v1/files?fileName=%E7%A5%A8%E6%8D%AE.png"
            && apiKey == "app-key" && uploaded.Size == 3 && uploaded.Id == "f1");

        var unlinked = await client.DownloadUnlinkedFileAsync("f1");
        check("对象存储跳转下载不转发应用密钥", unlinked.Content.SequenceEqual(new byte[] { 1, 2, 3 })
            && authentication[^2].ApiKey == "app-key" && authentication[^2].Bearer == null
            && path == "/objects/receipt" && apiKey == null && bearer == null);

        var linked = await client.DownloadFileAsync("f2", "person-jwt");
        check("已关联附件先以人员令牌验权，跳转时也不转发令牌", linked.Content.Length == 3
            && authentication[^2].Path == "/prefix/api/v1/files/f2"
            && authentication[^2].ApiKey == null && authentication[^2].Bearer == "person-jwt"
            && path == "/objects/receipt" && apiKey == null && bearer == null);

        CheckCallback(check);
    }

    /// <summary>
    /// 检查合法签名、正文篡改和错误签名三种回调。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckCallback(Action<string, bool> check)
    {
        var body = Encoding.UTF8.GetBytes("""{"eventId":"event-1","event":"approved","instanceId":"i1","serialNo":"SP-1","flowCode":"F","businessKey":"B","taskId":null,"status":"approved","data":{"amount":5},"fieldValues":null,"actionResults":{"action-1":{"orderNo":"PO-1"}},"occurredAt":"2026-10-20T08:00:00Z"}""");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("secret"));
        var signature = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(body));
        var result = ApprovalCallbackVerifier.VerifyAndParse(body, signature, "secret");
        check("正确回调签名可解析动作结果和稳定事件 ID", result.EventId == "event-1"
            && result.ActionResults?["action-1"] is Dictionary<string, object?> action
            && action["orderNo"] as string == "PO-1");

        var modified = (byte[])body.Clone();
        modified[modified.Length - 2] = (byte)'1';
        check("回调正文遭篡改时拒绝", RejectsCallback(modified, signature));
        check("回调签名错误时拒绝", RejectsCallback(body, "sha256=" + new string('0', 64)));
    }

    /// <summary>
    /// 判断验签器是否拒绝给定正文和签名。
    /// </summary>
    /// <param name="body">回调字节。</param>
    /// <param name="signature">签名。</param>
    /// <returns>签名无效时为 true。</returns>
    private static bool RejectsCallback(byte[] body, string signature)
    {
        try
        {
            ApprovalCallbackVerifier.VerifyAndParse(body, signature, "secret");
            return false;
        }
        catch (ApprovalServiceException error)
        {
            return error.ErrorCode == ApprovalErrorCodes.Unauthorized;
        }
    }

    /// <summary>
    /// 创建测试 JSON 响应。
    /// </summary>
    /// <param name="json">JSON 正文。</param>
    /// <returns>成功 HTTP 响应。</returns>
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <summary>
    /// 可注入 SDK 的极简 HTTP 处理器。
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        /// <summary>
        /// 按请求构造响应的函数。
        /// </summary>
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> response;

        /// <summary>
        /// 设置响应函数。
        /// </summary>
        /// <param name="response">响应函数。</param>
        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) => this.response = response;

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request);
    }
}
