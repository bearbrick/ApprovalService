using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 将接入应用的签名测试回调排入持久队列。
/// </summary>
/// <param name="administrators">当前应用管理员授权服务。</param>
/// <param name="repository">测试回调入队仓储。</param>
/// <param name="timeProvider">可替换的时间来源。</param>
public sealed class TestCallbackAppService(ClientAppAdminAppService administrators,
    ITestCallbackRepository repository, TimeProvider timeProvider)
{
    /// <summary>
    /// 从可信本地登录身份发起一次测试回调，不创建真实审批单。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="request">测试原因。</param>
    /// <param name="actorId">JWT 中的内部人员 ID。</param>
    /// <param name="isLocalSession">是否为本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>合成事件 ID 与排队状态。</returns>
    public async Task<AdminTestCallbackResponse> EnqueueAsync(string code, AdminTestCallbackRequest request,
        string actorId, bool isLocalSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128 ||
            code.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用编码无效。");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 2 or > 200)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请填写 2 到 200 个字符的测试原因。");
        }

        if (!await administrators.CanSendTestCallbackAsync(code, actorId, isLocalSession, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "当前人员不能发送该应用的测试回调。");
        }

        // 测试事件持有独立的合成身份，固定空业务字段，接收方只需验签确认。
        var eventId = CallbackEventCodes.TestEventIdPrefix + Guid.NewGuid().ToString("N");
        var now = timeProvider.GetUtcNow();
        var notification = new CallbackNotification
        {
            EventId = eventId,
            Event = CallbackEventCodes.IntegrationTest,
            InstanceId = eventId,
            SerialNo = string.Empty,
            FlowCode = string.Empty,
            BusinessKey = string.Empty,
            ClientAppCode = code,
            Status = CallbackNotification.TestStatus,
            Data = JsonSerializer.SerializeToElement(new { }),
            OccurredAt = now,
        };
        var result = await repository.EnqueueAsync(code, actorId, eventId,
            CallbackNotifications.Serialize(notification), reason, now, cancellationToken);
        return result switch
        {
            TestCallbackEnqueueResult.Queued => new AdminTestCallbackResponse { EventId = eventId },
            TestCallbackEnqueueResult.CallbackUrlMissing => throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "接入应用尚未配置回调地址。"),
            _ => throw new UserFriendlyException(ErrorCodes.Forbidden, "当前人员不能发送该应用的测试回调。"),
        };
    }
}
