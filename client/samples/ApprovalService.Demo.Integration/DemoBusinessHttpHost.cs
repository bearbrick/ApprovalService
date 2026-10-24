using System.Net;
using System.Runtime.Serialization;
using System.Text;
using ApprovalService.Client;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 本机业务后端：只监听回环地址，提供取数、预算校验、取人、采购动作和回调。
/// </summary>
public sealed class DemoBusinessHttpHost : IDisposable
{
    /// <summary>
    /// 请求正文上限，避免演示后端接收无限大小的请求。
    /// </summary>
    private const int MaximumBodyBytes = 1024 * 1024;

    /// <summary>
    /// 本机 HTTP 监听器。
    /// </summary>
    private readonly HttpListener listener = new();

    /// <summary>
    /// 签名密钥，来自演示运行配置，不写入业务文件。
    /// </summary>
    private readonly string signingSecret;

    /// <summary>
    /// 示例系统自己的业务单据。
    /// </summary>
    private readonly DemoDocumentStore store;

    /// <summary>
    /// 项目编码到本应用已绑定用户 ID 的映射。
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> projectManagers;

    /// <summary>
    /// 当前预算余额。
    /// </summary>
    private readonly decimal budgetRemaining;

    /// <summary>
    /// 演示业务动作在成功前故意失败的次数。
    /// </summary>
    private readonly int actionFailures;

    /// <summary>
    /// 监听任务，由 Dispose 停止。
    /// </summary>
    private Task? loop;

    /// <summary>
    /// 创建只允许本机访问的演示后端。
    /// </summary>
    /// <param name="prefix">完整回环 HTTP 前缀，末尾含斜杠。</param>
    /// <param name="signingSecret">与审批服务接入应用相同的签名密钥。</param>
    /// <param name="store">独立业务文件。</param>
    /// <param name="projectManagers">项目编码到业务用户 ID 的映射。</param>
    /// <param name="budgetRemaining">演示预算余额。</param>
    /// <param name="actionFailures">生成采购订单前故意失败的次数。</param>
    public DemoBusinessHttpHost(string prefix, string signingSecret, DemoDocumentStore store,
        IReadOnlyDictionary<string, string> projectManagers, decimal budgetRemaining, int actionFailures = 0)
    {
        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttp ||
            !address.IsLoopback || address.AbsolutePath != "/" || !prefix.EndsWith("/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
        {
            throw new ArgumentException("演示后端只允许带末尾斜杠的本机 HTTP 地址。", nameof(prefix));
        }

        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            throw new ArgumentException("演示后端必须配置签名密钥。", nameof(signingSecret));
        }

        if (actionFailures < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actionFailures), "失败演示次数不能为负。");
        }

        this.signingSecret = signingSecret;
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.projectManagers = projectManagers ?? throw new ArgumentNullException(nameof(projectManagers));
        this.budgetRemaining = budgetRemaining;
        this.actionFailures = actionFailures;
        listener.Prefixes.Add(prefix);
    }

    /// <summary>
    /// 启动本机 HTTP 后端。
    /// </summary>
    public void Start()
    {
        if (loop != null)
        {
            throw new InvalidOperationException("演示业务后端已经启动。");
        }

        listener.Start();
        loop = Task.Run(ServeAsync);
    }

    /// <summary>
    /// 停止监听，模拟没有业务后端时由客户端查询同步。
    /// </summary>
    public void Dispose()
    {
        if (listener.IsListening)
        {
            listener.Stop();
        }
        listener.Close();
        if (loop != null && loop.IsFaulted)
        {
            // 后台异常仍需在释放时暴露，不能悄悄吞掉程序缺陷。
            loop.GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// 顺序处理本机演示请求；停止监听时正常退出。
    /// </summary>
    /// <returns>监听结束。</returns>
    private async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (!listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException) when (!listener.IsListening)
            {
                break;
            }

            await HandleAsync(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 先限制正文并验签，再根据固定路径处理协议。
    /// </summary>
    /// <param name="context">本机 HTTP 请求。</param>
    /// <returns>响应已发送。</returns>
    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var requestId = request.Headers["X-Request-Id"];
        if (request.HttpMethod != "POST")
        {
            await ReplyAsync(context, 405, DemoHookJson.Write(new DemoErrorResponse { Message = "只接受 POST 请求。" }), requestId)
                .ConfigureAwait(false);
            return;
        }

        if (request.ContentLength64 > MaximumBodyBytes)
        {
            await ReplyAsync(context, 413, DemoHookJson.Write(new DemoErrorResponse { Message = "请求正文过大。" }), requestId)
                .ConfigureAwait(false);
            return;
        }

        byte[] body;
        try
        {
            body = await ReadBodyAsync(request).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            await ReplyAsync(context, 413, DemoHookJson.Write(new DemoErrorResponse { Message = "请求正文过大。" }), requestId)
                .ConfigureAwait(false);
            return;
        }

        var signatures = request.Headers.GetValues("X-Approval-Signature");
        if (signatures == null || signatures.Length != 1 ||
            !DemoHmacSignature.Verify(body, signatures[0], signingSecret))
        {
            await ReplyAsync(context, 401, DemoHookJson.Write(new DemoErrorResponse { Message = "请求签名无效。" }), requestId)
                .ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64)
        {
            await ReplyAsync(context, 400, DemoHookJson.Write(new DemoErrorResponse { Message = "请求 ID 无效。" }), null)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            var result = Process(request.Url?.AbsolutePath ?? string.Empty, body, requestId, signatures[0]);
            await ReplyAsync(context, result.StatusCode, result.Body, requestId).ConfigureAwait(false);
        }
        catch (SerializationException)
        {
            await ReplyAsync(context, 400, DemoHookJson.Write(new DemoErrorResponse { Message = "业务钩子 JSON 无效。" }), requestId)
                .ConfigureAwait(false);
        }
        catch (ApprovalServiceException)
        {
            await ReplyAsync(context, 400, DemoHookJson.Write(new DemoErrorResponse { Message = "回调正文无效。" }), requestId)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await ReplyAsync(context, 404, DemoHookJson.Write(new DemoErrorResponse { Message = ex.Message }), requestId)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 按登记的完整路径返回业务钩子结果。
    /// </summary>
    /// <param name="path">请求路径。</param>
    /// <param name="body">已验签原始正文。</param>
    /// <param name="requestId">已验证请求 ID。</param>
    /// <param name="signature">原始签名头，供 SDK 回调解析器复核。</param>
    /// <returns>HTTP 状态与响应正文。</returns>
    private DemoHttpResult Process(string path, byte[] body, string requestId, string signature)
    {
        switch (path)
        {
            case "/data":
                {
                    var input = DemoHookJson.Read<DemoDataHookRequest>(body);
                    if (input.RequestId != requestId)
                    {
                        return new DemoHttpResult(400, DemoHookJson.Write(new DemoErrorResponse { Message = "请求 ID 与正文不一致。" }));
                    }

                    if (input.FlowCode == "EXPENSE")
                    {
                        var claim = store.FindExpense(input.BusinessKey) ?? throw new InvalidOperationException("费用报销原单不存在。");
                        return new DemoHttpResult(200, DemoHookJson.Write(new DemoExpenseDataResponse
                        {
                            Title = claim.Title,
                            Data = new DemoExpenseHookData
                            {
                                Title = claim.Title,
                                Category = claim.Category,
                                ClaimAmount = claim.ClaimAmount,
                                Receipt = claim.ReceiptFileId,
                                Lines = claim.Lines
                            },
                        }));
                    }

                    if (input.FlowCode != "PURCHASE_REQUEST")
                    {
                        return new DemoHttpResult(400, DemoHookJson.Write(new DemoErrorResponse { Message = "示例业务系统不支持该流程。" }));
                    }

                    var purchase = store.FindPurchase(input.BusinessKey) ?? throw new InvalidOperationException("采购原单不存在。");
                    return new DemoHttpResult(200, DemoHookJson.Write(new DemoPurchaseDataResponse
                    {
                        Title = purchase.Title,
                        Data = new DemoPurchaseHookData
                        {
                            Title = purchase.Title,
                            Requester = purchase.Initiator,
                            Department = purchase.Department,
                            BaseAmount = purchase.BaseAmount,
                            TaxAmount = purchase.TaxAmount,
                            Items = purchase.Items
                        },
                    }));
                }
            case "/validate":
                {
                    var input = DemoHookJson.Read<DemoValidationHookRequest>(body);
                    var purchase = input.FlowCode == "PURCHASE_REQUEST" ? store.FindPurchase(input.BusinessKey) : null;
                    var shortage = purchase != null && purchase.WithinBudget ?
                        purchase.BaseAmount + purchase.TaxAmount - budgetRemaining : 0m;
                    return new DemoHttpResult(200, DemoHookJson.Write(new DemoValidationResponse
                    {
                        Allowed = shortage <= 0m,
                        Reason = shortage > 0m ? $"预算不足，还差 {shortage:0.00} 元。" : null,
                    }));
                }
            case "/assignees":
                {
                    var input = DemoHookJson.Read<DemoAssigneeHookRequest>(body);
                    var project = input.SourceValue;
                    if (string.IsNullOrWhiteSpace(project))
                    {
                        project = input.FlowCode == "EXPENSE" ? null : store.FindPurchase(input.BusinessKey)?.ProjectCode;
                    }

                    var people = project != null && projectManagers.TryGetValue(project, out var manager) ?
                        new List<string> { manager } : new List<string>();
                    return new DemoHttpResult(200, DemoHookJson.Write(new DemoAssigneeResponse { UserIds = people }));
                }
            case "/action":
                {
                    var input = DemoHookJson.Read<DemoActionHookRequest>(body);
                    if (input.FlowCode != "PURCHASE_REQUEST" || input.ActionCode != "createPurchaseOrder")
                    {
                        return new DemoHttpResult(400, DemoHookJson.Write(new DemoErrorResponse { Message = "示例业务动作不支持该流程或动作。" }));
                    }

                    var action = store.RunPurchaseAction(input.EventId, input.BusinessKey, actionFailures);
                    return new DemoHttpResult(200, DemoHookJson.Write(new DemoActionResponse
                    {
                        Success = action.Success,
                        Message = action.Success ? null : "演示采购接口暂时失败，请按原事件 ID 重试。",
                        Result = action.OrderNumber == null ? null : new DemoOrderResult { OrderNo = action.OrderNumber },
                    }));
                }
            case "/callback":
                {
                    var notification = ApprovalCallbackVerifier.VerifyAndParse(body, signature, signingSecret);
                    store.ApplyCallback(notification);
                    return new DemoHttpResult(200, DemoHookJson.Write(new DemoAcceptedResponse { Accepted = true }));
                }
            default:
                return new DemoHttpResult(404, DemoHookJson.Write(new DemoErrorResponse { Message = "业务钩子地址不存在。" }));
        }
    }

    /// <summary>
    /// 读取最多一兆字节的原始请求正文。
    /// </summary>
    /// <param name="request">HTTP 请求。</param>
    /// <returns>未修改的正文字节。</returns>
    private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await request.InputStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
        {
            if (stream.Length + count > MaximumBodyBytes)
            {
                throw new InvalidDataException("业务钩子请求正文过大。");
            }

            stream.Write(buffer, 0, count);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 对原始响应字节签名并回显请求 ID。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="status">状态码。</param>
    /// <param name="body">待签名正文。</param>
    /// <param name="requestId">可选请求 ID。</param>
    /// <returns>写入完成。</returns>
    private async Task ReplyAsync(HttpListenerContext context, int status, byte[] body, string? requestId)
    {
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers.Add("X-Approval-Signature", DemoHmacSignature.Sign(body, signingSecret));
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            response.Headers.Add("X-Request-Id", requestId);
        }

        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        response.Close();
    }
}

/// <summary>
/// 本机 HTTP 路由返回的状态和已序列化正文。
/// </summary>
internal sealed class DemoHttpResult
{
    /// <summary>
    /// 创建 HTTP 结果。
    /// </summary>
    /// <param name="statusCode">响应状态码。</param>
    /// <param name="body">响应字节。</param>
    internal DemoHttpResult(int statusCode, byte[] body)
    {
        StatusCode = statusCode;
        Body = body;
    }

    /// <summary>
    /// 响应状态码。
    /// </summary>
    internal int StatusCode
    {
        get;
    }

    /// <summary>
    /// 响应字节。
    /// </summary>
    internal byte[] Body
    {
        get;
    }
}

/// <summary>
/// 回调被演示业务系统持久接收的响应。
/// </summary>
[DataContract]
internal sealed class DemoAcceptedResponse
{
    /// <summary>
    /// 是否接收。
    /// </summary>
    [DataMember(Name = "accepted")]
    public bool Accepted
    {
        get; set;
    }
}

/// <summary>
/// 演示业务后端的安全错误正文。
/// </summary>
[DataContract]
internal sealed class DemoErrorResponse
{
    /// <summary>
    /// 中文错误说明。
    /// </summary>
    [DataMember(Name = "message")]
    public string Message { get; set; } = string.Empty;
}
