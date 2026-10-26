namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 设置一项本人或管理员代设的审批委托。
/// </summary>
public sealed class CenterDelegationRequest
{
    /// <summary>
    /// 代理人的内部人员 ID。
    /// </summary>
    public string DelegatePersonId { get; set; } = string.Empty;
    /// <summary>
    /// UTC 生效起点，包含此刻。
    /// </summary>
    public DateTimeOffset StartsAtUtc
    {
        get; set;
    }
    /// <summary>
    /// UTC 结束时刻，不包含此刻。
    /// </summary>
    public DateTimeOffset EndsAtUtc
    {
        get; set;
    }
    /// <summary>
    /// 限定流程编码；空集合表示全部流程。
    /// </summary>
    public List<string> FlowCodes { get; set; } = [];
}

/// <summary>
/// 已持久保存的委托规则。
/// </summary>
public sealed class CenterDelegationResponse
{
    /// <summary>
    /// 规则 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// 委托人的内部人员 ID。
    /// </summary>
    public string PrincipalPersonId { get; set; } = string.Empty;
    /// <summary>
    /// 代理人的内部人员 ID。
    /// </summary>
    public string DelegatePersonId { get; set; } = string.Empty;
    /// <summary>
    /// UTC 生效起点。
    /// </summary>
    public DateTimeOffset StartsAtUtc
    {
        get; set;
    }
    /// <summary>
    /// UTC 结束时刻。
    /// </summary>
    public DateTimeOffset EndsAtUtc
    {
        get; set;
    }
    /// <summary>
    /// 限定流程编码；空集合表示全部流程。
    /// </summary>
    public List<string> FlowCodes { get; set; } = [];
}

/// <summary>
/// 已有待办的逐项转委托请求。
/// </summary>
public sealed class CenterDelegateTasksRequest
{
    /// <summary>
    /// 要转委托的任务 ID，最多一百项。
    /// </summary>
    public List<string> TaskIds { get; set; } = [];
    /// <summary>
    /// 管理员执行时必须填写的原因。
    /// </summary>
    public string? Reason
    {
        get; set;
    }
}

/// <summary>
/// 已有待办单项转委托的结果。
/// </summary>
public sealed class CenterDelegateTaskResult
{
    /// <summary>
    /// 原任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>
    /// 是否成功。
    /// </summary>
    public bool Succeeded
    {
        get; set;
    }
    /// <summary>
    /// 失败时的稳定错误码。
    /// </summary>
    public string? Code
    {
        get; set;
    }
    /// <summary>
    /// 失败时可读的中文说明。
    /// </summary>
    public string? Message
    {
        get; set;
    }
}
