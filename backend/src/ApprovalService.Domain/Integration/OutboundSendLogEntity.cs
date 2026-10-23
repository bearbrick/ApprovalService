namespace ApprovalService.Domain.Integration;

/// <summary>
/// 每次向业务系统发送回调的结果，不保存密钥、请求正文或业务系统响应正文。
/// </summary>
public sealed class OutboundSendLogEntity
{
    /// <summary>
    /// 发送记录 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 可供业务系统去重的稳定事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 本次发送所属接入应用。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;

    /// <summary>
    /// 待发队列累计尝试次数。
    /// </summary>
    public int Attempt
    {
        get; set;
    }

    /// <summary>
    /// 目标 HTTP 地址。
    /// </summary>
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>
    /// 本次发送时间。
    /// </summary>
    public DateTimeOffset SentAt
    {
        get; set;
    }

    /// <summary>
    /// 业务系统返回的 HTTP 状态；未收到响应时为空。
    /// </summary>
    public int? HttpStatus
    {
        get; set;
    }

    /// <summary>
    /// 是否成功送达。
    /// </summary>
    public bool Succeeded
    {
        get; set;
    }

    /// <summary>
    /// 失败类型，不记录可能包含敏感数据的异常消息。
    /// </summary>
    public string? ErrorCode
    {
        get; set;
    }
}
