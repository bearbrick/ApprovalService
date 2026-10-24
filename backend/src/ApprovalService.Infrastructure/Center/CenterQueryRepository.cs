using System.Data;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
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
        if (query.From.HasValue)
        {
            instances = instances.Where(instance => instance.StartedAt >= query.From.Value);
        }
        if (query.To.HasValue)
        {
            instances = instances.Where(instance => instance.StartedAt < query.To.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // 内容搜索须在参与关系和字段权限过滤后计算命中数与分页，不对原始 JSON 做命中判定。
            var searched = await SearchAsync(db, instances, scope.PersonId, query, cancellationToken);
            await AttachReadStateAsync(db, searched.Items, scope.PersonId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return searched;
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
        await AttachReadStateAsync(db, records, scope.PersonId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CenterPage { Total = count, Items = records };
    }

    /// <summary>
    /// 分批扫描已授权单据，先裁剪可见字段，再计算搜索命中总数与分页。
    /// </summary>
    /// <param name="db">当前读取事务的数据库上下文。</param>
    /// <param name="instances">已按人员、应用及结构条件过滤的单据查询。</param>
    /// <param name="personId">可信登录人员 ID。</param>
    /// <param name="query">搜索与分页条件。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>可见内容命中后的分页结果。</returns>
    private static async Task<CenterPage> SearchAsync(ApprovalDbContext db, IQueryable<InstanceEntity> instances,
        string personId, CenterQuery query, CancellationToken cancellationToken)
    {
        const int batchSize = 100;
        var search = query.Search!.Trim();
        var ordered = instances.OrderByDescending(instance => instance.IsUrgent)
            .ThenByDescending(instance => instance.StartedAt).ThenBy(instance => instance.Id);
        var count = 0;
        var offset = 0;
        var selected = new List<CenterRecord>();
        while (true)
        {
            var records = await Project(db, ordered.Skip(offset).Take(batchSize))
                .OrderByDescending(record => record.Instance.IsUrgent)
                .ThenByDescending(record => record.Instance.StartedAt).ThenBy(record => record.Instance.Id)
                .ToArrayAsync(cancellationToken);
            if (records.Length == 0)
            {
                break;
            }
            var ids = records.Select(record => record.Instance.Id).ToArray();
            var tasks = await db.ApprovalTasks.AsNoTracking().Where(task => ids.Contains(task.InstanceId))
                .OrderBy(task => task.Id).ToArrayAsync(cancellationToken);
            foreach (var record in records)
            {
                record.Tasks = tasks.Where(task => task.InstanceId == record.Instance.Id).ToArray();
                var instance = record.Instance;
                var metadataMatch = instance.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    instance.SerialNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    instance.BusinessKey.Contains(search, StringComparison.OrdinalIgnoreCase);
                if (!metadataMatch)
                {
                    var definition = JsonSerializer.Deserialize<FlowDefinition>(record.FlowVersion.DefinitionJson, ApprovalJson.Options)!;
                    var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(record.FlowVersion.GraphJson, ApprovalJson.Options)!;
                    var visible = CenterFieldVisibility.Filter(record, personId,
                        definition.Data.Fields.Select(field => field.Key), definition.Data.Fields, graph);
                    if (!CenterFieldVisibility.ContainsValue(visible, search))
                    {
                        continue;
                    }
                }
                count++;
                if (count > query.Skip && selected.Count < query.Take)
                {
                    selected.Add(record);
                }
            }
            offset += records.Length;
            if (records.Length < batchSize)
            {
                break;
            }
        }
        return new CenterPage { Total = count, Items = selected };
    }

    /// <inheritdoc />
    public async Task<CenterRecord?> FindAsync(CenterScope scope, string instanceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var record = await Project(db, VisibleDetail(db, scope, instanceId)).SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            return null;
        }
        record.Tasks = await db.ApprovalTasks.AsNoTracking().Where(task => task.InstanceId == instanceId)
            .OrderBy(task => task.Id).ToArrayAsync(cancellationToken);
        record.Events = await db.OperationEvents.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .OrderBy(item => item.Sequence).ToArrayAsync(cancellationToken);
        record.ReadThroughSequence = await db.CenterReadCursors.AsNoTracking()
            .Where(item => item.InstanceId == instanceId && item.PersonId == scope.PersonId)
            .Select(item => item.ReadThroughSequence).SingleOrDefaultAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    /// <inheritdoc />
    public async Task<long?> MarkReadAsync(CenterScope scope, string instanceId, long seenThroughSequence,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await VisibleDetail(db, scope, instanceId).AnyAsync(cancellationToken))
        {
            return null;
        }
        var latest = await db.OperationEvents.AsNoTracking().Where(item => item.InstanceId == instanceId)
            .MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0;
        var seen = Math.Min(seenThroughSequence, latest);
        var cursor = await db.CenterReadCursors.SingleOrDefaultAsync(item =>
            item.InstanceId == instanceId && item.PersonId == scope.PersonId, cancellationToken);
        if (cursor is null)
        {
            db.CenterReadCursors.Add(new CenterReadCursorEntity
            {
                InstanceId = instanceId,
                PersonId = scope.PersonId,
                ReadThroughSequence = seen,
            });
        }
        else if (cursor.ReadThroughSequence < seen)
        {
            cursor.ReadThroughSequence = seen;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Math.Max(cursor?.ReadThroughSequence ?? 0, seen);
    }

    /// <summary>
    /// 在同一读取事务内装配列表页的个人游标及其后的活动记录。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="records">已授权的本页记录。</param>
    /// <param name="personId">可信登录人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>装配完成。</returns>
    private static async Task AttachReadStateAsync(ApprovalDbContext db, IReadOnlyList<CenterRecord> records,
        string personId, CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return;
        }
        var ids = records.Select(record => record.Instance.Id).ToArray();
        var cursors = await db.CenterReadCursors.AsNoTracking().Where(item =>
            item.PersonId == personId && ids.Contains(item.InstanceId)).ToArrayAsync(cancellationToken);
        var known = cursors.ToDictionary(item => item.InstanceId, item => item.ReadThroughSequence, StringComparer.Ordinal);
        var firstUnread = records.Min(record => known.GetValueOrDefault(record.Instance.Id));
        var events = await db.OperationEvents.AsNoTracking().Where(item =>
            ids.Contains(item.InstanceId) && item.Sequence > firstUnread).ToArrayAsync(cancellationToken);
        foreach (var record in records)
        {
            record.ReadThroughSequence = known.GetValueOrDefault(record.Instance.Id);
            record.Events = events.Where(item => item.InstanceId == record.Instance.Id &&
                item.Sequence > record.ReadThroughSequence).ToArray();
        }
    }

    /// <summary>
    /// 详情读取和标记已读共享同一保密参与关系与嵌入来源边界。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="scope">可信人员及来源应用。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <returns>仅当前人员可查看的一张单据查询。</returns>
    private static IQueryable<InstanceEntity> VisibleDetail(ApprovalDbContext db, CenterScope scope, string instanceId)
    {
        var person = scope.PersonId;
        // 保密流程也只按实际参与关系开放；管理员和额外查看人不隐式拥有本入口权限。
        return AppScope(db, scope).Where(instance => instance.Id == instanceId &&
            (instance.InitiatorId == person || instance.SubmittedById == person ||
                db.ApprovalTasks.Any(task => task.InstanceId == instance.Id && task.Status == EngineTaskStatuses.Pending &&
                    (task.AssigneeId == person || task.DelegatedForId == person)) ||
                db.InstanceParticipants.Any(participant => participant.InstanceId == instance.Id && participant.PersonId == person)));
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
