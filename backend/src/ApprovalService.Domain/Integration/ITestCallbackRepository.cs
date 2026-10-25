namespace ApprovalService.Domain.Integration;

/// <summary>
/// 无真实审批单的接入测试回调入队边界。
/// </summary>
public interface ITestCallbackRepository
{
    /// <summary>
    /// 在同一事务内重验应用与操作者、保存待发项和审计原因。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="actorId">本地登录人员 ID。</param>
    /// <param name="eventId">合成事件 ID。</param>
    /// <param name="payloadJson">固定的测试回调正文。</param>
    /// <param name="reason">测试原因。</param>
    /// <param name="occurredAt">排队时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>入队结果。</returns>
    Task<TestCallbackEnqueueResult> EnqueueAsync(string code, string actorId, string eventId,
        string payloadJson, string reason, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 测试回调入队的可修正结果。
/// </summary>
public enum TestCallbackEnqueueResult
{
    /// <summary>
    /// 已排入持久队列。
    /// </summary>
    Queued,

    /// <summary>
    /// 应用不存在或已停用。
    /// </summary>
    AppUnavailable,

    /// <summary>
    /// 当前人员没有该应用的管理权限。
    /// </summary>
    Forbidden,

    /// <summary>
    /// 应用未登记回调地址。
    /// </summary>
    CallbackUrlMissing,
}
