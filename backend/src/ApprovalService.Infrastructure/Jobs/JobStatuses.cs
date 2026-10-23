namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 数据库中待发项与定时任务的执行状态。
/// </summary>
public static class JobStatuses
{
    /// <summary>
    /// 尚未领取或等待重试。
    /// </summary>
    public const string Pending = "pending";

    /// <summary>
    /// 已由一个实例取得限时租约。
    /// </summary>
    public const string Leased = "leased";

    /// <summary>
    /// 已成功处理。
    /// </summary>
    public const string Completed = "completed";
}
