namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 流程管理员以原事件身份请求人工重试自动动作。
/// </summary>
/// <param name="EventId">自动动作原事件 ID，可包含路径分隔符。</param>
public sealed record AdminActionRetryRequest(string EventId);
