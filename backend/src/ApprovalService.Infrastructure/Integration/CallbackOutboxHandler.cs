using System.Net;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 在审批事务之外发送持久回调；后台队列负责失败后的租约和退避重试。
/// </summary>
/// <param name="factory">读取接入应用并记录每次发送的上下文工厂。</param>
/// <param name="protector">签名密钥解密器。</param>
/// <param name="outbound">统一签名 HTTP 出站适配层。</param>
/// <param name="timeProvider">可替换时钟。</param>
public sealed class CallbackOutboxHandler(IDbContextFactory<ApprovalDbContext> factory,
    Application.Integration.IIntegrationSecretProtector protector, SignedHttpOutboundAdapter outbound,
    TimeProvider timeProvider) : IOutboxHandler
{
    /// <inheritdoc />
    public string Kind => "callback";

    /// <inheritdoc />
    public async Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var notification = JsonSerializer.Deserialize<CallbackNotification>(item.PayloadJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("回调待发项缺少通知正文。");
        if (notification.EventId != item.EventId || notification.InstanceId != item.InstanceId)
        {
            throw new InvalidOperationException("回调正文与待发项的事件身份不一致。");
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var isIntegrationTest = notification.Event == CallbackEventCodes.IntegrationTest;
        if (isIntegrationTest && (!item.EventId.StartsWith(CallbackEventCodes.TestEventIdPrefix,
            StringComparison.Ordinal) ||
            notification.InstanceId != notification.EventId ||
            string.IsNullOrWhiteSpace(notification.ClientAppCode) ||
            notification.SerialNo.Length != 0 || notification.FlowCode.Length != 0 ||
            notification.BusinessKey.Length != 0 || notification.Status != CallbackNotification.TestStatus ||
            notification.Data.ValueKind != JsonValueKind.Object || notification.Data.EnumerateObject().Any() ||
            notification.TaskId is not null || notification.FieldValues is not null ||
            notification.ActionResults is not null))
        {
            throw new InvalidOperationException("测试回调正文与合成事件约定不一致。");
        }

        // 测试事件没有审批单，应用编码只能来自已持久化的测试通知正文。
        var appCode = isIntegrationTest ? notification.ClientAppCode! :
            await db.Instances.AsNoTracking().Where(instance => instance.Id == item.InstanceId)
                .Select(instance => instance.ClientAppCode).SingleAsync(cancellationToken);
        var app = await db.ClientApps.AsNoTracking().SingleAsync(client => client.Code == appCode, cancellationToken);
        if (!app.IsEnabled)
        {
            // 保留待发项并由队列重试，管理员重新启用应用后仍可送达同一事件。
            db.OutboundSendLogs.Add(new OutboundSendLogEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                EventId = item.EventId,
                ClientAppCode = appCode,
                Attempt = item.Attempts,
                TargetUrl = app.CallbackUrl ?? string.Empty,
                SentAt = timeProvider.GetUtcNow(),
                ErrorCode = "CLIENT_APP_DISABLED",
            });
            await db.SaveChangesAsync(cancellationToken);
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用已停用，回调暂缓发送并保留待发事件。");
        }

        if (string.IsNullOrWhiteSpace(app.CallbackUrl))
        {
            if (isIntegrationTest)
            {
                // 测试回调必须真的到达接收端；排队后地址被清除时不能伪报成功。
                db.OutboundSendLogs.Add(new OutboundSendLogEntity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    EventId = item.EventId,
                    ClientAppCode = appCode,
                    Attempt = item.Attempts,
                    TargetUrl = string.Empty,
                    SentAt = timeProvider.GetUtcNow(),
                    ErrorCode = "CALLBACK_URL_MISSING",
                });
                await db.SaveChangesAsync(cancellationToken);
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "接入应用回调地址已移除，测试回调将保留并重试。");
            }

            // 未登记回调地址的应用使用主动查询状态；该事件仍在操作记录中留存。
            return;
        }

        var log = new OutboundSendLogEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EventId = item.EventId,
            ClientAppCode = appCode,
            Attempt = item.Attempts,
            TargetUrl = app.CallbackUrl,
            SentAt = timeProvider.GetUtcNow(),
        };
        try
        {
            var secret = protector.Unprotect(app.SigningSecretCiphertext);
            var response = await outbound.PostAsync(app.CallbackUrl, item.PayloadJson, secret,
                item.EventId, cancellationToken);
            log.HttpStatus = response.StatusCode;
            log.Succeeded = true;
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // 仅写错误类别和状态码；远端异常正文可能包含审批数据，不入日志。
            log.HttpStatus = error is HttpRequestException { StatusCode: { } status }
                ? (int)(HttpStatusCode)status : null;
            log.ErrorCode = error.GetType().Name;
            db.Set<OutboundSendLogEntity>().Add(log);
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }

        db.Set<OutboundSendLogEntity>().Add(log);
        await db.SaveChangesAsync(cancellationToken);
    }
}
