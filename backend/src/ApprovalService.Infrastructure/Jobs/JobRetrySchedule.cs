namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 待发项和定时任务共用的失败重试间隔。
/// </summary>
public static class JobRetrySchedule
{
    /// <summary>
    /// 根据已失败次数计算下次重试间隔；从一分钟起，逐步延长至一天。
    /// </summary>
    /// <param name="failedAttempts">已失败的执行次数，从一开始。</param>
    /// <returns>下次重试前的等待时间。</returns>
    public static TimeSpan AfterFailure(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failedAttempts);
        return failedAttempts switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(30),
            4 => TimeSpan.FromHours(2),
            _ => TimeSpan.FromHours(Math.Min(24, 2 * Math.Pow(2, Math.Min(failedAttempts - 4, 4)))),
        };
    }
}
