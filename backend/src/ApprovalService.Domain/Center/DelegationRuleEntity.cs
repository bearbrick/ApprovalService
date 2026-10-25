namespace ApprovalService.Domain.Center;

/// <summary>
/// 一项审批委托规则，生效区间使用 UTC 左闭右开语义。
/// </summary>
public sealed class DelegationRuleEntity
{
    /// <summary>
    /// 规则标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 委托人的内部人员标识。
    /// </summary>
    public string PrincipalId { get; set; } = string.Empty;

    /// <summary>
    /// 代理人的内部人员标识。
    /// </summary>
    public string DelegateId { get; set; } = string.Empty;

    /// <summary>
    /// 生效开始时间，包含该时刻。
    /// </summary>
    public DateTimeOffset StartsAtUtc
    {
        get; set;
    }

    /// <summary>
    /// 生效结束时间，不包含该时刻。
    /// </summary>
    public DateTimeOffset EndsAtUtc
    {
        get; set;
    }

    /// <summary>
    /// 限定的流程编码；空集合代表全部流程。
    /// </summary>
    public List<string> FlowCodes { get; set; } = [];
}
