using System.Data;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Reporting;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Reporting;

/// <summary>
/// 只读取审批单、任务投影与不可变事件；不读取业务数据或敏感详情。
/// </summary>
/// <param name="factory">按部署配置选择的数据库上下文工厂。</param>
public sealed class ReportingFactsRepository(IDbContextFactory<ApprovalDbContext> factory) : IReportingFactsRepository
{
    /// <inheritdoc />
    public Task<ReportingFacts> ReadAsync(ReportingFactsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.InstanceStartedFromUtc.Offset != TimeSpan.Zero ||
            query.InstanceStartedUntilUtc.Offset != TimeSpan.Zero ||
            query.InstanceStartedFromUtc >= query.InstanceStartedUntilUtc)
        {
            throw new ArgumentException("审批单发起时间范围必须是非空的 UTC 半开区间。", nameof(query));
        }

        return ReadCoreAsync(db => db.Instances.AsNoTracking()
            .Where(item => item.StartedAt >= query.InstanceStartedFromUtc &&
                item.StartedAt < query.InstanceStartedUntilUtc),
            query.FlowCode, query.ClientAppCode, query.InitiatorDepartmentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReportingFacts> ReadForMetricsAsync(ReportingMetricsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.FromUtc.Offset != TimeSpan.Zero || query.UntilUtc.Offset != TimeSpan.Zero ||
            query.AsOfUtc.Offset != TimeSpan.Zero || query.FromUtc >= query.UntilUtc || query.AsOfUtc == default)
        {
            throw new ArgumentException("报表时间范围必须是非空的 UTC 半开区间，且须指定 UTC 观察时间。", nameof(query));
        }

        return ReadCoreAsync(db => db.Instances.AsNoTracking().Where(instance =>
            (instance.StartedAt >= query.FromUtc && instance.StartedAt < query.UntilUtc) ||
            (instance.EndedAt >= query.FromUtc && instance.EndedAt < query.UntilUtc) ||
            db.ApprovalTasks.Any(task => task.InstanceId == instance.Id &&
                ((task.CompletedAt >= query.FromUtc && task.CompletedAt < query.UntilUtc) ||
                 (task.DueAt >= query.FromUtc && task.DueAt < query.UntilUtc && task.DueAt <= query.AsOfUtc) ||
                 (task.Status == EngineTaskStatuses.Pending && task.CompletedAt == null)))),
            query.FlowCode, query.ClientAppCode, query.InitiatorDepartmentId, cancellationToken);
    }

    /// <summary>
    /// 在同一数据库快照内读取两种查询共同使用的最小化事实。
    /// </summary>
    /// <param name="selectInstances">选择可能影响报表的审批单。</param>
    /// <param name="flowCode">可选流程编码。</param>
    /// <param name="clientAppCode">可选来源应用编码。</param>
    /// <param name="departmentId">可选发起部门快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>不含标题和业务数据的统计事实。</returns>
    private async Task<ReportingFacts> ReadCoreAsync(Func<ApprovalDbContext, IQueryable<InstanceEntity>> selectInstances,
        string? flowCode, string? clientAppCode, string? departmentId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var instances = selectInstances(db);
        if (flowCode is not null)
        {
            instances = instances.Where(item => item.FlowCode == flowCode);
        }

        if (clientAppCode is not null)
        {
            instances = instances.Where(item => item.ClientAppCode == clientAppCode);
        }

        if (departmentId is not null)
        {
            instances = instances.Where(item => item.InitiatorDepartmentId == departmentId);
        }

        // 仅读取统计字段，避免业务数据及保密详情越过未来报表授权层。
        var instanceFacts = await instances.OrderBy(item => item.StartedAt).ThenBy(item => item.Id)
            .Select(item => new ReportingInstanceFact
            {
                Id = item.Id,
                FlowCode = item.FlowCode,
                FlowVersionId = item.FlowVersionId,
                ClientAppCode = item.ClientAppCode,
                InitiatorDepartmentId = item.InitiatorDepartmentId,
                Status = item.Status,
                StartedAt = item.StartedAt,
                EndedAt = item.EndedAt,
            }).ToListAsync(cancellationToken);
        if (instanceFacts.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ReportingFacts();
        }

        // 保密性由发起时锁定的版本决定；找不到或损坏版本时不能默认公开。
        var versionIds = instanceFacts.Select(item => item.FlowVersionId).Distinct(StringComparer.Ordinal).ToArray();
        var versions = await db.FlowVersions.AsNoTracking()
            .Where(item => versionIds.Contains(item.Id))
            .Select(item => new { item.Id, item.GraphJson })
            .ToListAsync(cancellationToken);
        var confidentialByVersion = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var version in versions)
        {
            FlowExecutionGraph graph;
            try
            {
                graph = JsonSerializer.Deserialize<FlowExecutionGraph>(version.GraphJson, ApprovalJson.Options) ??
                    throw new InvalidDataException("流程执行图为空，不能判断报表保密范围。");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("流程执行图损坏，不能判断报表保密范围。", exception);
            }

            confidentialByVersion.Add(version.Id, graph.IsConfidential);
        }

        foreach (var item in instanceFacts)
        {
            if (!confidentialByVersion.TryGetValue(item.FlowVersionId, out var isConfidential))
            {
                throw new InvalidDataException("审批单的锁定流程版本不存在，不能判断报表保密范围。");
            }

            item.IsConfidential = isConfidential;
        }

        var instanceIds = instanceFacts.Select(item => item.Id).ToArray();
        var tasks = await db.ApprovalTasks.AsNoTracking()
            .Where(item => instanceIds.Contains(item.InstanceId))
            .OrderBy(item => item.InstanceId).ThenBy(item => item.Id)
            .Select(item => new ReportingTaskFact
            {
                Id = item.Id,
                InstanceId = item.InstanceId,
                NodeId = item.NodeId,
                AssigneeId = item.AssigneeId,
                DelegatedForId = item.DelegatedForId,
                Status = item.Status,
                DueAt = item.DueAt,
                CompletedAt = item.CompletedAt,
            }).ToListAsync(cancellationToken);
        var events = await db.OperationEvents.AsNoTracking()
            .Where(item => instanceIds.Contains(item.InstanceId) &&
                (item.Kind == "taskCreated" || item.Kind == "taskActivated"))
            .OrderBy(item => item.InstanceId).ThenBy(item => item.Sequence)
            .Select(item => new { item.Id, item.InstanceId, item.Kind, item.PayloadJson, item.OccurredAt })
            .ToListAsync(cancellationToken);
        var createdFacts = new List<ReportingTaskCreatedFact>(events.Count);
        var activatedFacts = new List<ReportingTaskActivatedFact>();
        foreach (var entry in events)
        {
            var payload = JsonSerializer.Deserialize<EngineEvent>(entry.PayloadJson, ApprovalJson.Options);
            if (string.IsNullOrWhiteSpace(payload?.TaskId))
            {
                // 损坏的审计日志不能悄悄变成准确的任务时长。
                throw new InvalidDataException("任务创建事件缺少任务 ID，无法生成可信统计事实。");
            }

            if (entry.Kind == "taskActivated")
            {
                activatedFacts.Add(new ReportingTaskActivatedFact
                {
                    TaskId = payload.TaskId,
                    OccurredAt = entry.OccurredAt,
                });
                continue;
            }

            createdFacts.Add(new ReportingTaskCreatedFact
            {
                EventId = entry.Id,
                InstanceId = entry.InstanceId,
                TaskId = payload.TaskId,
                NodeId = payload.NodeId,
                OccurredAt = entry.OccurredAt,
            });
        }

        await transaction.CommitAsync(cancellationToken);
        return new ReportingFacts
        {
            Instances = instanceFacts,
            Tasks = tasks,
            TaskCreatedEvents = createdFacts,
            TaskActivatedEvents = activatedFacts,
        };
    }
}
