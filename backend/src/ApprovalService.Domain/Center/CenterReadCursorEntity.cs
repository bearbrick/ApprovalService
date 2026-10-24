namespace ApprovalService.Domain.Center;

/// <summary>
/// 每位人员对一张审批单已看过的最大可见活动序号。
/// </summary>
public sealed class CenterReadCursorEntity
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 已认证的内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// 已看过的最大事件序号，只能前进。
    /// </summary>
    public long ReadThroughSequence
    {
        get; set;
    }
}
