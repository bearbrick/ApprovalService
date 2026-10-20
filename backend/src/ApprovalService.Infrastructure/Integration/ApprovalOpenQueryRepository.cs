using System.Data;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 只读开放接口投影；待办数量在数据库聚合，避免逐项加载审批数据。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class ApprovalOpenQueryRepository(IDbContextFactory<ApprovalDbContext> factory) : IApprovalOpenQueryRepository
{
    /// <inheritdoc />
    public async Task<InstanceEntity?> LoadInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Instances.AsNoTracking().SingleOrDefaultAsync(item => item.Id == instanceId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InstanceEntity?> LoadActiveAsync(string flowCode, string businessKey,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Instances.AsNoTracking().Where(item => item.FlowCode == flowCode &&
                item.BusinessKey == businessKey &&
                (item.Status == EngineInstanceStatuses.Running || item.Status == EngineInstanceStatuses.AwaitingInitiator))
            .OrderByDescending(item => item.StartedAt).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstanceEntity>> ListInstancesAsync(string flowCode, string businessKey, string clientAppCode,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Instances.AsNoTracking().Where(item => item.FlowCode == flowCode && item.BusinessKey == businessKey &&
                item.ClientAppCode == clientAppCode)
            .OrderByDescending(item => item.StartedAt).ThenByDescending(item => item.Id).ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OpenInstanceDetails?> LoadDetailsAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        // 审批单状态、任务投影和操作记录来自同一持久快照，避免并发审批时详情自相矛盾。
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var instance = await db.Instances.AsNoTracking().SingleOrDefaultAsync(item => item.Id == instanceId, cancellationToken);
        if (instance is null)
        {
            return null;
        }

        var tasks = await db.ApprovalTasks.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var events = await db.OperationEvents.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .OrderBy(item => item.Sequence).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OpenInstanceDetails { Instance = instance, Tasks = tasks, Events = events };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenTaskRecord>> ListPendingTasksAsync(string personId, string? clientAppCode,
        int skip, int take, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await (from task in db.ApprovalTasks.AsNoTracking()
                      join instance in db.Instances.AsNoTracking() on task.InstanceId equals instance.Id
                      join version in db.FlowVersions.AsNoTracking() on instance.FlowVersionId equals version.Id
                      where task.AssigneeId == personId && task.Status == EngineTaskStatuses.Pending &&
                          (clientAppCode == null || instance.ClientAppCode == clientAppCode)
                      orderby instance.IsUrgent descending, task.DueAt, instance.StartedAt descending, task.Id
                      select new OpenTaskRecord { Task = task, Instance = instance, FlowVersion = version })
            .Skip(skip).Take(take).ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OpenTaskRecord?> LoadTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await (from task in db.ApprovalTasks.AsNoTracking()
                      join instance in db.Instances.AsNoTracking() on task.InstanceId equals instance.Id
                      join version in db.FlowVersions.AsNoTracking() on instance.FlowVersionId equals version.Id
                      where task.Id == taskId
                      select new OpenTaskRecord { Task = task, Instance = instance, FlowVersion = version })
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OpenTaskCounts> CountPendingTasksAsync(string personId, string? clientAppCode, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = from task in db.ApprovalTasks.AsNoTracking()
                    join instance in db.Instances.AsNoTracking() on task.InstanceId equals instance.Id
                    where task.AssigneeId == personId && task.Status == EngineTaskStatuses.Pending &&
                        (clientAppCode == null || instance.ClientAppCode == clientAppCode)
                    select new
                    {
                        task.DueAt,
                        instance.IsUrgent
                    };
        return await query.GroupBy(_ => 1).Select(group => new OpenTaskCounts
        {
            Pending = group.Count(),
            Urgent = group.Count(item => item.IsUrgent),
            Overdue = group.Count(item => item.DueAt != null && item.DueAt < now),
        }).SingleOrDefaultAsync(cancellationToken) ?? new OpenTaskCounts();
    }
}
