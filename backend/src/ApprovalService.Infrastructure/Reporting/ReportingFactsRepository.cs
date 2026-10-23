using System.Data;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Domain.Reporting;
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
    public async Task<ReportingFacts> ReadAsync(ReportingFactsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.InstanceStartedFromUtc.Offset != TimeSpan.Zero ||
            query.InstanceStartedUntilUtc.Offset != TimeSpan.Zero ||
            query.InstanceStartedFromUtc >= query.InstanceStartedUntilUtc)
        {
            throw new ArgumentException("审批单发起时间范围必须是非空的 UTC 半开区间。", nameof(query));
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var instances = db.Instances.AsNoTracking()
            .Where(item => item.StartedAt >= query.InstanceStartedFromUtc &&
                item.StartedAt < query.InstanceStartedUntilUtc);
        if (query.FlowCode is not null)
        {
            instances = instances.Where(item => item.FlowCode == query.FlowCode);
        }

        if (query.ClientAppCode is not null)
        {
            instances = instances.Where(item => item.ClientAppCode == query.ClientAppCode);
        }

        if (query.InitiatorDepartmentId is not null)
        {
            instances = instances.Where(item => item.InitiatorDepartmentId == query.InitiatorDepartmentId);
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
            .Where(item => instanceIds.Contains(item.InstanceId) && item.Kind == "taskCreated")
            .OrderBy(item => item.InstanceId).ThenBy(item => item.Sequence)
            .Select(item => new { item.Id, item.InstanceId, item.PayloadJson, item.OccurredAt })
            .ToListAsync(cancellationToken);
        var createdFacts = new List<ReportingTaskCreatedFact>(events.Count);
        foreach (var entry in events)
        {
            var payload = JsonSerializer.Deserialize<EngineEvent>(entry.PayloadJson, ApprovalJson.Options);
            if (string.IsNullOrWhiteSpace(payload?.TaskId))
            {
                // 损坏的审计日志不能悄悄变成准确的任务时长。
                throw new InvalidDataException("任务创建事件缺少任务 ID，无法生成可信统计事实。");
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
        };
    }
}
