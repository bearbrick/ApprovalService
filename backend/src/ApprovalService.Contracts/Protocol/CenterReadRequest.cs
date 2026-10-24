namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 仅确认本次详情响应实际包含的活动，避免详情显示后新活动被误标已读。
/// </summary>
public sealed class CenterReadRequest
{
    /// <summary>
    /// 详情响应中已显示的最大活动序号；零表示当时没有可见活动。
    /// </summary>
    public long SeenThroughSequence
    {
        get; set;
    }
}
