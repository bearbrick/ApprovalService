namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 业务系统以本应用已绑定人员身份发起实例级操作。
/// </summary>
public sealed class OpenInstanceCommandRequest
{
    /// <summary>
    /// 当前接入应用已绑定的业务人员 ID。
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// 终止审批时必填的原因；撤回和催办可省略。
    /// </summary>
    public string? Reason
    {
        get; set;
    }
}
