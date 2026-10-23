using System.Data;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Center;

/// <summary>
/// 将人员参与关系及嵌入应用范围直接下推到数据库查询，不先加载全量再过滤。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class CenterQueryRepository(IDbContextFactory<ApprovalDbContext> factory) : ICenterQueryRepository
{
    /// <inheritdoc />
    public async Task<CenterPage> ListAsync(CenterScope scope, CenterQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var instances = AppScope(db, scope);
        var person = scope.PersonId;
        instances = query.Section switch
        {
            CenterSections.Todo => instances.Where(instance => db.ApprovalTasks.Any(task =>
                task.InstanceId == instance.Id && task.Status == EngineTaskStatuses.Pending &&
                (task.AssigneeId == person || task.DelegatedForId == person))),
            CenterSections.Done => instances.Where(instance => db.InstanceParticipants.Any(participant =>
                participant.InstanceId == instance.Id && participant.PersonId == person && participant.Kind == InstanceParticipantKinds.TaskActor)),
            CenterSections.Copy => instances.Where(instance => db.InstanceParticipants.Any(participant =>
                participant.InstanceId == instance.Id && participant.PersonId == person && participant.Kind == InstanceParticipantKinds.CopyRecipient)),
            CenterSections.Started => instances.Where(instance => instance.InitiatorId == person || instance.SubmittedById == person),
            _ => throw new ArgumentException("不支持的审批列表分组。", nameof(query)),
        };
        if (!string.IsNullOrWhiteSpace(query.FlowCode))
        {
            instances = instances.Where(instance => instance.FlowCode == query.FlowCode);
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            instances = instances.Where(instance => instance.Status == query.Status);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // 不搜索原始业务 JSON，防止隐藏字段通过搜索是否命中泄露。
            var search = query.Search.Trim();
            instances = instances.Where(instance => instance.Title.Contains(search) ||
                instance.SerialNo.Contains(search) || instance.BusinessKey.Contains(search));
        }
        if (query.From.HasValue)
        {
            instances = instances.Where(instance => instance.StartedAt >= query.From.Value);
        }
        if (query.To.HasValue)
        {
            instances = instances.Where(instance => instance.StartedAt < query.To.Value);
        }
        var count = await instances.CountAsync(cancellationToken);
        var page = instances.OrderByDescending(instance => instance.IsUrgent)
            .ThenByDescending(instance => instance.StartedAt).ThenBy(instance => instance.Id)
            .Skip(query.Skip).Take(query.Take);
        var records = await Project(db, page).OrderByDescending(record => record.Instance.IsUrgent)
            .ThenByDescending(record => record.Instance.StartedAt).ThenBy(record => record.Instance.Id)
            .ToArrayAsync(cancellationToken);
        var ids = records.Select(record => record.Instance.Id).ToArray();
        var tasks = await db.ApprovalTasks.AsNoTracking().Where(task => ids.Contains(task.InstanceId))
            .OrderBy(task => task.Id).ToArrayAsync(cancellationToken);
        foreach (var record in records)
        {
            record.Tasks = tasks.Where(task => task.InstanceId == record.Instance.Id).ToArray();
        }
        await transaction.CommitAsync(cancellationToken);
        return new CenterPage { Total = count, Items = records };
    }

    /// <inheritdoc />
    public async Task<CenterRecord?> FindAsync(CenterScope scope, string instanceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var person = scope.PersonId;
        // 已发布的保密流程也只按实际参与关系开放；管理员和额外查看人不隐式拥有本入口权限。
        var visible = AppScope(db, scope).Where(instance => instance.Id == instanceId &&
            (instance.InitiatorId == person || instance.SubmittedById == person ||
                db.ApprovalTasks.Any(task => task.InstanceId == instance.Id && task.Status == EngineTaskStatuses.Pending &&
                    (task.AssigneeId == person || task.DelegatedForId == person)) ||
                db.InstanceParticipants.Any(participant => participant.InstanceId == instance.Id && participant.PersonId == person)));
        var record = await Project(db, visible).SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            return null;
        }
        record.Tasks = await db.ApprovalTasks.AsNoTracking().Where(task => task.InstanceId == instanceId)
            .OrderBy(task => task.Id).ToArrayAsync(cancellationToken);
        record.Events = await db.OperationEvents.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .OrderBy(item => item.Sequence).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    /// <summary>
    /// 在所有计数、分页和详情读取之前限定嵌入应用来源。
    /// </summary>
    /// <param name="db">本次读取的数据库上下文。</param>
    /// <param name="scope">可信登录范围。</param>
    /// <returns>已限定来源应用的查询。</returns>
    private static IQueryable<InstanceEntity> AppScope(ApprovalDbContext db, CenterScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.PersonId) ||
            (scope.ClientAppCode is not null && string.IsNullOrWhiteSpace(scope.ClientAppCode)))
        {
            throw new ArgumentException("查询必须提供完整的可信登录范围。", nameof(scope));
        }
        return db.Instances.AsNoTracking().Where(instance => scope.ClientAppCode == null ||
            instance.ClientAppCode == scope.ClientAppCode);
    }

    /// <summary>
    /// 从已授权单据关联固定流程版本，不改变调用方的筛选条件。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="instances">已授权且可分页的审批单查询。</param>
    /// <returns>可执行的只读投影。</returns>
    private static IQueryable<CenterRecord> Project(ApprovalDbContext db, IQueryable<InstanceEntity> instances) =>
        from instance in instances
        join version in db.FlowVersions.AsNoTracking() on instance.FlowVersionId equals version.Id
        join flow in db.Flows.AsNoTracking() on version.FlowId equals flow.Id
        select new CenterRecord
        {
            Instance = instance,
            FlowVersion = version,
            FlowName = flow.Name,
            SourceName = db.ClientApps.Where(app => app.Code == instance.ClientAppCode)
                .Select(app => app.Name).FirstOrDefault() ?? instance.ClientAppCode,
            InitiatorName = db.People.Where(person => person.Id == instance.InitiatorId)
                .Select(person => person.Name).FirstOrDefault() ?? instance.InitiatorId,
        };
}
