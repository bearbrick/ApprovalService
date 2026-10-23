using System.Text.Json;
using ApprovalService.Domain.Runtime;

namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 从已持久的引擎事件正文判别业务自动动作。
/// </summary>
internal static class ActionOutboxPayload
{
    /// <summary>
    /// 检查原事件是否为待执行自动动作，不依赖模糊字符串包含。
    /// </summary>
    /// <param name="item">待发项及原始事件正文。</param>
    /// <returns>是自动动作待发项时为真。</returns>
    public static bool IsPendingAction(OutboxItemEntity item)
    {
        if (item.Kind != "engineEvent")
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("kind", out var kind) &&
                kind.ValueKind == JsonValueKind.String && kind.GetString() == "actionPending";
        }
        catch (JsonException)
        {
            // 损坏的旧待发项继续沿通用失败规则处理，不误判为可由管理员重试的动作。
            return false;
        }
    }
}
