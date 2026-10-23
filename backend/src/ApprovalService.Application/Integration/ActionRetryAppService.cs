using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 供经权限核对的管理用例对原业务动作事件人工重试一次。
/// </summary>
/// <param name="repository">原持久动作待发项。</param>
/// <param name="timeProvider">可替换时钟。</param>
public sealed class ActionRetryAppService(IActionRetryRepository repository, TimeProvider timeProvider)
{
    /// <summary>
    /// 用原事件 ID 重排一次，不允许复制待发项或增加自动重试预算。
    /// </summary>
    /// <param name="eventId">原自动动作触发事件 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    public async Task RetryAsync(string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "自动动作事件 ID 不能为空。");
        }

        if (!await repository.ExistsAsync(eventId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "自动动作事件不存在。");
        }

        if (!await repository.RequeueAsync(eventId, timeProvider.GetUtcNow(), cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "自动动作尚未转为待管理员处理，或正在重试中。");
        }
    }
}
