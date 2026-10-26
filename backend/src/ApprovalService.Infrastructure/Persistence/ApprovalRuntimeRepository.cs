using System.Data;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
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
        var tasks = await db.ApprovalTasks.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var participants = await db.InstanceParticipants.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .ToListAsync(cancellationToken);
        var lastEvent = await db.OperationEvents.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .OrderByDescending(item => item.Sequence).FirstOrDefaultAsync(cancellationToken);
        return new RuntimeSnapshot
        {
            Instance = instance,
            FlowVersion = version,
            Tasks = tasks,
            Participants = participants,
            LastEvent = lastEvent,
        };
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
        if (commit.FileLinks.Any(link => string.IsNullOrWhiteSpace(link.FileId) ||
                link.ClientAppCode != commit.Instance.ClientAppCode) ||
            commit.FileLinks.Select(link => link.FileId).Distinct(StringComparer.Ordinal).Count() != commit.FileLinks.Count)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "附件引用或接入应用归属不正确。");
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (commit.ExpectedVersion is null)
        {
            db.Instances.Add(commit.Instance);
            if (IsActive(commit.Instance.Status))
            {
                db.ActiveInstanceKeys.Add(new ActiveInstanceKeyEntity
                {
                    FlowCode = commit.Instance.FlowCode,
                    BusinessKey = commit.Instance.BusinessKey,
                    InstanceId = commit.Instance.Id,
                });
            }
        }
        else
        {
            // 快照是无追踪读取的；显式指定原始版本让 EF 在 UPDATE 的 WHERE 中检查它。
            db.Instances.Attach(commit.Instance);
            var entry = db.Entry(commit.Instance);
            entry.State = EntityState.Modified;
            entry.Property(item => item.Version).OriginalValue = commit.ExpectedVersion.Value;
            if (!IsActive(commit.Instance.Status))
            {
                var activeKey = await db.ActiveInstanceKeys.SingleOrDefaultAsync(item =>
                    item.FlowCode == commit.Instance.FlowCode && item.BusinessKey == commit.Instance.BusinessKey &&
                    item.InstanceId == commit.Instance.Id, cancellationToken);
                if (activeKey is not null)
                {
                    // 结束与状态、事件同事务提交，历史审批单随后可以用同一业务单号再发起。
                    db.ActiveInstanceKeys.Remove(activeKey);
                }
            }
        }

        var existingTasks = await db.ApprovalTasks.Where(item => item.InstanceId == commit.Instance.Id)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var retainedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in commit.Tasks)
        {
            retainedIds.Add(task.Id);
            if (existingTasks.TryGetValue(task.Id, out var existing))
            {
                // 撤销同意会把原任务恢复待办；只有仍已结束的任务才能沿用历史完成投影。
                var stillCompleted = task.Status is not (EngineTaskStatuses.Pending or EngineTaskStatuses.Queued);
                if (stillCompleted && task.CompletedAt is null && existing.CompletedAt is not null)
                {
                    task.CompletedAt = existing.CompletedAt;
                }
                if (stillCompleted && task.Opinion is null && existing.Opinion is not null)
                {
                    // 后续节点命令会重建全部任务投影，已完成任务的意见必须持续保留。
                    task.Opinion = existing.Opinion;
                }
                if (stillCompleted && task.SignatureFileId is null && existing.SignatureFileId is not null)
                {
                    // 后续节点重建任务投影时保留原操作所冻结的签名引用。
                    task.SignatureFileId = existing.SignatureFileId;
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
        if (commit.Participants.Count > 0)
        {
            if (commit.Participants.Any(item => item.InstanceId != commit.Instance.Id ||
                    string.IsNullOrWhiteSpace(item.PersonId) ||
                    item.Kind is not (InstanceParticipantKinds.CopyRecipient or InstanceParticipantKinds.TaskActor) ||
                    !commit.Events.Any(operationEvent => operationEvent.Id == item.EventId)))
            {
                throw new InvalidOperationException("参与人投影必须来源于本次审批事件。");
            }

            var existingParticipants = await db.InstanceParticipants.Where(item =>
                    item.InstanceId == commit.Instance.Id)
                .Select(item => new { item.PersonId, item.Kind }).ToArrayAsync(cancellationToken);
            var known = existingParticipants.Select(item => (item.PersonId, item.Kind)).ToHashSet();
            db.InstanceParticipants.AddRange(commit.Participants.Where(item => known.Add((item.PersonId, item.Kind))));
        }
        // 文件关联含条件更新，需要显式事务覆盖实例插入、事件和全部文件；先写实例以满足外键。
        await using var fileTransaction = commit.FileLinks.Count > 0
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            // EF Core 在一次 SaveChanges 中为多表写入建立数据库事务，冲突时全部回滚。
            await db.SaveChangesAsync(cancellationToken);
            foreach (var link in commit.FileLinks)
            {
                // 条件更新避免并发发起或清理抢先时覆盖已经关联到其他审批单的文件。
                var updated = await db.StoredFiles.Where(item => item.Id == link.FileId &&
                        item.UploadedByClientAppCode == link.ClientAppCode && item.UploadedById == null &&
                        item.InstanceId == null && item.CleanupOwner == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.InstanceId, commit.Instance.Id)
                        .SetProperty(item => item.LinkedAt, link.LinkedAt), cancellationToken);
                if (updated != 1)
                {
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                        "附件不存在、已被使用或不属于当前接入应用。");
                }
            }

            if (fileTransaction is not null)
            {
                await fileTransaction.CommitAsync(cancellationToken);
            }

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (fileTransaction is not null)
            {
                await fileTransaction.RollbackAsync(cancellationToken);
            }

            return false;
        }
        catch (DbUpdateException) when (commit.ExpectedVersion is null && IsActive(commit.Instance.Status))
        {
            if (fileTransaction is not null)
            {
                await fileTransaction.RollbackAsync(cancellationToken);
            }

            // 只有确认另一审批单已占用在途键时才转成可恢复的冲突；其他数据库错误原样暴露。
            await using var check = await factory.CreateDbContextAsync(cancellationToken);
            if (await check.ActiveInstanceKeys.AsNoTracking().AnyAsync(item =>
                item.FlowCode == commit.Instance.FlowCode && item.BusinessKey == commit.Instance.BusinessKey,
                cancellationToken))
            {
                return false;
            }

            throw;
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

    /// <summary>
    /// 退回发起人也属于在途；其他状态释放业务单号占用。
    /// </summary>
    /// <param name="status">引擎状态。</param>
    /// <returns>仍占用业务单号为真。</returns>
    private static bool IsActive(string status) => status is EngineInstanceStatuses.Running or EngineInstanceStatuses.AwaitingInitiator;
}
