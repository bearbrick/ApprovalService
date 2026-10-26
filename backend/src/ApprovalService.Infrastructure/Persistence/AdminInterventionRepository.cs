using System.Data;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 在当前数据库授权快照内查询运行干预记录。
/// </summary>
/// <param name="factory">上下文工厂。</param>
public sealed class AdminInterventionRepository(IDbContextFactory<ApprovalDbContext> factory)
    : IAdminInterventionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RuntimeSnapshot>> ReadAsync(string actorId, bool isLocal,
        string? instanceId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var identity = await FlowManagementRepository.ReadIdentityAsync(db, actorId, cancellationToken);
        if (!isLocal || !identity.IsActive || !(identity.IsOrganizationAdmin || identity.IsFlowAdmin))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "只有本地流程管理员可以干预审批。");
        }
        var departments = identity.DirectDepartmentIds.ToArray();
        var query = db.Instances.AsNoTracking().Where(instance => db.Flows.Any(flow =>
            flow.Id == instance.FlowId && (identity.IsOrganizationAdmin ||
                departments.Contains(flow.OwnerDepartmentId!))));
        if (instanceId is not null)
        {
            query = query.Where(item => item.Id == instanceId);
        }
        else
        {
            query = query.Where(item => item.Status == EngineInstanceStatuses.Running ||
                item.Status == EngineInstanceStatuses.AwaitingInitiator);
        }
        var result = new List<RuntimeSnapshot>();
        // 分批读取直到找到一百条已授权记录，不让不可见保密单占用可见列表名额。
        var offset = 0;
        while (result.Count < 100)
        {
            var batch = await query.OrderByDescending(item => item.StartedAt).ThenBy(item => item.Id)
                .Skip(offset).Take(100).ToArrayAsync(cancellationToken);
            if (batch.Length == 0)
            {
                break;
            }
            offset += batch.Length;
            foreach (var instance in batch)
            {
                if (!await AdminInterventionAccess.CanAccessAsync(db, actorId, instance, cancellationToken))
                {
                    continue;
                }
                result.Add(new RuntimeSnapshot
                {
                    Instance = instance,
                    FlowVersion = await db.FlowVersions.AsNoTracking().SingleAsync(
                        item => item.Id == instance.FlowVersionId, cancellationToken),
                    Tasks = await db.ApprovalTasks.AsNoTracking().Where(item => item.InstanceId == instance.Id)
                        .ToArrayAsync(cancellationToken),
                });
                if (result.Count == 100)
                {
                    break;
                }
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}

