using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Jobs;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 内部引擎事件保留出站游标；需要外部动作的事件在协议明确前保持可重试。
/// </summary>
public sealed class EngineEventOutboxHandler : IOutboxHandler
{
    /// <inheritdoc />
    public string Kind => "engineEvent";

    /// <inheritdoc />
    public Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken)
    {
        var source = JsonSerializer.Deserialize<EngineEvent>(item.PayloadJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("引擎待发项缺少事件正文。");
        if (source.Id != item.EventId)
        {
            throw new InvalidOperationException("引擎待发项事件身份不一致。");
        }

        if (source.Kind is "actionPending" or "subprocessRequested")
        {
            // 自动节点与子流程必须由已登记的执行器确认完成，不能静默吞掉事件。
            throw new InvalidOperationException("外部动作协议尚未确认，待发项将在配置后重试。");
        }

        return Task.CompletedTask;
    }
}
