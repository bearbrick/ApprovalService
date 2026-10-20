using System.Data;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 每次操作创建独立上下文；状态、任务、事件和待发项由一次 SaveChanges 原子提交。
/// </summary>
/// <param name="factory">当前数据库提供程序的上下文工厂。</param>
public sealed class ApprovalRuntimeRepository(IDbContextFactory<ApprovalDbContext> factory) : IApprovalRuntimeRepository
{
    /// <inheritdoc />
    public async Task<RuntimeSnapshot?> LoadAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var instance = await db.Instances.AsNoTracking().SingleOrDefaultAsync(item => item.Id == instanceId, cancellationToken);
        if (instance is null)
        {
            return null;
        }
        var version = await db.FlowVersions.AsNoTracking().SingleAsync(item => item.Id == instance.FlowVersionId, cancellationToken);
        var lastEvent = await db.OperationEvents.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .OrderByDescending(item => item.Sequence).FirstOrDefaultAsync(cancellationToken);
        return new RuntimeSnapshot { Instance = instance, FlowVersion = version, LastEvent = lastEvent };
    }

    /// <inheritdoc />
    public async Task<FlowVersionEntity?> LoadVersionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.FlowVersions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == versionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DepartureTaskRef>> ListDepartureTasksAsync(string assigneeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assigneeId);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await (from task in db.ApprovalTasks.AsNoTracking()
                      join instance in db.Instances.AsNoTracking() on task.InstanceId equals instance.Id
                      where task.AssigneeId == assigneeId && instance.Status == EngineInstanceStatuses.Running &&
                          (task.Status == EngineTaskStatuses.Pending || task.Status == EngineTaskStatuses.Queued)
                      orderby task.InstanceId, task.Id
                      select new DepartureTaskRef { InstanceId = task.InstanceId, TaskId = task.Id })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CommitAsync(RuntimeCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.Instance.Version != commit.ExpectedVersion.GetValueOrDefault() + 1)
        {
            throw new InvalidOperationException("审批单提交版本必须比读取版本增加一。");
        }

        var lastSequence = commit.Instance.LastEventSequence;
        var lastHash = commit.Instance.LastEventHash;
        foreach (var operationEvent in commit.Events)
        {
            // 锚点必须连续推进，不能让丢失尾部记录后的命令接在错误位置。
            if (operationEvent.InstanceId != commit.Instance.Id ||
                operationEvent.Sequence != lastSequence + 1 ||
                !string.Equals(operationEvent.PreviousHash, lastHash, StringComparison.Ordinal) ||
                !string.Equals(operationEvent.Hash, OperationHash.Compute(operationEvent), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("操作记录与审批单事件锚点不一致。");
            }

            lastSequence = operationEvent.Sequence;
            lastHash = operationEvent.Hash;
        }

        commit.Instance.LastEventSequence = lastSequence;
        commit.Instance.LastEventHash = lastHash;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (commit.ExpectedVersion is null)
        {
            db.Instances.Add(commit.Instance);
        }
        else
        {
            // 快照是无追踪读取的；显式指定原始版本让 EF 在 UPDATE 的 WHERE 中检查它。
            db.Instances.Attach(commit.Instance);
            var entry = db.Entry(commit.Instance);
            entry.State = EntityState.Modified;
            entry.Property(item => item.Version).OriginalValue = commit.ExpectedVersion.Value;
        }

        var existingTasks = await db.ApprovalTasks.Where(item => item.InstanceId == commit.Instance.Id)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var retainedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in commit.Tasks)
        {
            retainedIds.Add(task.Id);
            if (existingTasks.TryGetValue(task.Id, out var existing))
            {
                // 任务是引擎状态的查询投影，更新时保留已经记录的完成时间。
                if (task.CompletedAt is null && existing.CompletedAt is not null)
                {
                    task.CompletedAt = existing.CompletedAt;
                }
                db.Entry(existing).CurrentValues.SetValues(task);
            }
            else
            {
                db.ApprovalTasks.Add(task);
            }
        }

        foreach (var previous in existingTasks.Values.Where(item => !retainedIds.Contains(item.Id)))
        {
            db.ApprovalTasks.Remove(previous);
        }

        db.OperationEvents.AddRange(commit.Events);
        db.OutboxItems.AddRange(commit.OutboxItems);
        try
        {
            // EF Core 在一次 SaveChanges 中为多表写入建立数据库事务，冲突时全部回滚。
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<EventChainSnapshot?> LoadEventChainAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        // 两次查询放在可重复读的事务快照内，避免并发提交夹在锚点与事件读取之间。
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var anchor = await db.Instances.AsNoTracking().Where(item => item.Id == instanceId)
            .Select(item => new { item.LastEventSequence, item.LastEventHash })
            .SingleOrDefaultAsync(cancellationToken);
        if (anchor is null)
        {
            return null;
        }

        var events = await db.OperationEvents.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new EventChainSnapshot
        {
            LastEventSequence = anchor.LastEventSequence,
            LastEventHash = anchor.LastEventHash,
            Events = events,
        };
    }

    /// <inheritdoc />
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken);
    }
}
