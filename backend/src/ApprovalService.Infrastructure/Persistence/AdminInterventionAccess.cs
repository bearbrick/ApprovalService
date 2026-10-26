using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 运行干预的当前角色、直属部门和保密参与关系校验。
/// </summary>
internal static class AdminInterventionAccess
{
    /// <summary>
    /// 同一数据库上下文中重新核对指定运行实例的管理范围。
    /// </summary>
    /// <param name="db">当前事务上下文。</param>
    /// <param name="actorId">可信本地人员。</param>
    /// <param name="instance">待操作实例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>拥有管理权及必要参与关系时为真。</returns>
    internal static async Task<bool> CanAccessAsync(ApprovalDbContext db, string actorId,
        InstanceEntity instance, CancellationToken cancellationToken)
    {
        var identity = await FlowManagementRepository.ReadIdentityAsync(db, actorId, cancellationToken);
        var flow = await db.Flows.AsNoTracking().SingleOrDefaultAsync(item => item.Id == instance.FlowId,
            cancellationToken);
        if (flow is null || !FlowManagementAccessRules.CanManage(identity.IsActive, true,
            identity.IsOrganizationAdmin, identity.IsFlowAdmin, identity.DirectDepartmentIds, flow))
        {
            return false;
        }
        var version = await db.FlowVersions.AsNoTracking().SingleAsync(item => item.Id == instance.FlowVersionId,
            cancellationToken);
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(version.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("发布执行图为空。");
        if (!graph.IsConfidential)
        {
            return true;
        }
        // 保密单只允许实际参与人，不由管理角色扩张查看范围。
        return instance.InitiatorId == actorId || instance.SubmittedById == actorId ||
            await db.ApprovalTasks.AnyAsync(task => task.InstanceId == instance.Id &&
                task.Status == EngineTaskStatuses.Pending &&
                (task.AssigneeId == actorId || task.DelegatedForId == actorId), cancellationToken) ||
            await db.InstanceParticipants.AnyAsync(item => item.InstanceId == instance.Id &&
                item.PersonId == actorId, cancellationToken);
    }

    /// <summary>
    /// 在最终提交事务中重验管理权限和接手人在职状态。
    /// </summary>
    /// <param name="db">最终提交事务。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="instanceId">目标实例。</param>
    /// <param name="targetId">可选接手人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>校验完成。</returns>
    internal static async Task RequireAsync(ApprovalDbContext db, string actorId, string instanceId,
        string? targetId, CancellationToken cancellationToken)
    {
        var instance = await db.Instances.AsNoTracking().SingleOrDefaultAsync(item => item.Id == instanceId,
            cancellationToken);
        if (instance is null || !await CanAccessAsync(db, actorId, instance, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在或当前账号无权干预。");
        }
        if (targetId is not null && !await db.People.AnyAsync(item => item.Id == targetId && item.IsActive,
            cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接手人员不存在或已停用。");
        }
    }
}

