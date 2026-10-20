namespace ApprovalService.Client.Internal;

/// <summary>
/// HTTP 头，是后端 Contracts 里 HeaderNames 的副本：名字和值都要一样，由后端检查程序核对（AGENTS.md §6.4）。
/// </summary>
internal static class Protocol
{
    /// <summary>
    /// 接入应用的 API Key。
    /// </summary>
    public const string ApiKey = "X-Api-Key";

    /// <summary>
    /// SDK 每次请求生成的请求 ID，服务原样返回并记进日志。
    /// </summary>
    public const string RequestId = "X-Request-Id";

    /// <summary>
    /// 服务返回的链路 ID，反馈问题时提供。
    /// </summary>
    public const string TraceId = "X-Trace-Id";

    /// <summary>
    /// 回调请求体的签名（sha256=十六进制小写）。
    /// </summary>
    public const string Signature = "X-Approval-Signature";

    /// <summary>
    /// 已授权的发布流程列表。
    /// </summary>
    public const string Flows = "/api/v1/flows";

    /// <summary>
    /// 流程审批数据定义。
    /// </summary>
    public const string FlowSchema = "/api/v1/flows/{code}/schema";

    /// <summary>
    /// 发起审批和按业务单号查询历史。
    /// </summary>
    public const string Instances = "/api/v1/instances";

    /// <summary>
    /// 按审批单 ID 查询详情。
    /// </summary>
    public const string Instance = "/api/v1/instances/{id}";

    /// <summary>
    /// 发起人撤回在途审批。
    /// </summary>
    public const string InstanceWithdraw = "/api/v1/instances/{id}/withdraw";

    /// <summary>
    /// 发起人催办当前任务。
    /// </summary>
    public const string InstanceRemind = "/api/v1/instances/{id}/remind";

    /// <summary>
    /// 人员待办数量。
    /// </summary>
    public const string TaskCount = "/api/v1/tasks/count";

    /// <summary>
    /// 人员待办列表。
    /// </summary>
    public const string Tasks = "/api/v1/tasks";

    /// <summary>
    /// 人工任务操作。
    /// </summary>
    public const string TaskActions = "/api/v1/tasks/{id}/actions";
}
