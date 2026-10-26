namespace ApprovalService.Application.Notifications;

/// <summary>
/// 为每次运行命令取得同一份期限与提醒计算快照。
/// </summary>
public interface ITaskDeadlinePolicyProvider
{
    /// <summary>
    /// 读取当前日历并建立不可变策略。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本次命令独享的日历策略。</returns>
    Task<TaskDeadlinePolicy> GetAsync(CancellationToken cancellationToken = default);
}
