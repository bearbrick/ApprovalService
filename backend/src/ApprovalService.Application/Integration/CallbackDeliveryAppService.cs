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
}
