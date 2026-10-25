namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 有权管理应用的本地人员请求发送签名测试回调。
/// </summary>
public sealed class AdminTestCallbackRequest
{
    /// <summary>
    /// 可审阅的测试原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 测试回调已进入持久队列的确认。
/// </summary>
public sealed class AdminTestCallbackResponse
{
    /// <summary>
    /// 接收方用于去重的合成事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 固定为 queued，表示只完成排队。
    /// </summary>
    public string Status { get; set; } = "queued";
}
