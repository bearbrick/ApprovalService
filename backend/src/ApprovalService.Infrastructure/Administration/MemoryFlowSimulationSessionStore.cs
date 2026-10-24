using System.Collections.Concurrent;
using ApprovalService.Application.Administration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Infrastructure.Administration;

/// <summary>
/// 有界的进程内模拟会话容器；进程重启会自然丢失全部模拟。
/// </summary>
internal sealed class MemoryFlowSimulationSessionStore : IFlowSimulationSessionStore
{
    /// <summary>
    /// 同一进程允许的最多会话数。
    /// </summary>
    private const int MaximumSessions = 128;

    /// <summary>
    /// 会话表。
    /// </summary>
    private readonly ConcurrentDictionary<string, FlowSimulationSession> sessions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Add(FlowSimulationSession session, DateTimeOffset now)
    {
        foreach (var item in sessions)
        {
            if (item.Value.ExpiresAt <= now)
            {
                sessions.TryRemove(item.Key, out _);
            }
        }

        if (sessions.Count >= MaximumSessions)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "当前模拟会话已满，请稍后再试。");
        }

        if (!sessions.TryAdd(session.Id, session))
        {
            throw new InvalidOperationException("随机模拟会话 ID 重复。");
        }
    }

    /// <inheritdoc />
    public FlowSimulationSession? Find(string id) => sessions.TryGetValue(id, out var session) ? session : null;

    /// <inheritdoc />
    public void Remove(string id) => sessions.TryRemove(id, out _);
}
