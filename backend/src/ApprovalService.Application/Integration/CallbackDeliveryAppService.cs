using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 仅供已认证管理用例使用的回调手动重发服务，不暴露匿名开放路由。
/// </summary>
/// <param name="repository">持久待发项仓储。</param>
/// <param name="timeProvider">可替换时钟。</param>
public sealed class CallbackDeliveryAppService(ICallbackDeliveryRepository repository, TimeProvider timeProvider)
{
    /// <summary>
    /// 重排原事件的回调，不生成新事件或新正文。
    /// </summary>
    /// <param name="eventId">业务系统用于去重的事件 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    public async Task ResendAsync(string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "回调事件 ID 不能为空。");
        }

        if (!await repository.ExistsAsync(eventId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "回调事件不存在。");
        }

        if (!await repository.RequeueAsync(eventId, timeProvider.GetUtcNow(), cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "回调正在发送，请稍后重试。");
        }
    }

    /// <summary>
    /// 管理员带原因手动重发原事件回调，操作与原因在同一事务持久留痕。
    /// </summary>
    /// <param name="eventId">原始回调事件 ID。</param>
    /// <param name="actorId">本地组织管理员人员 ID。</param>
    /// <param name="reason">本次重发原因。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public async Task ResendAdminAsync(string eventId, string actorId, string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(actorId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "回调事件和管理员身份不能为空。");
        }

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 2 or > 200)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请填写 2 到 200 个字符的重发原因。");
        }

        if (!await repository.ExistsAsync(eventId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "回调事件不存在。");
        }

        if (!await repository.RequeueWithAuditAsync(eventId, timeProvider.GetUtcNow(), actorId, reason,
            cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "回调正在发送，请稍后重试。");
        }
    }
}
