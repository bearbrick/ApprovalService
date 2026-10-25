namespace ApprovalService.Domain.Center;

/// <summary>
/// 审批人保存的一条个人常用意见。
/// </summary>
public sealed class CenterOpinionEntity
{
    /// <summary>
    /// 常用意见标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 所属内部人员标识。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// 意见正文。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间，使用 UTC。
    /// </summary>
    public DateTime CreatedAtUtc
    {
        get; set;
    }
}
