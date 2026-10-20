using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Jobs;

/// <summary>
/// 消费可信组织变更同事务排入的离职任务，逐在途任务执行引擎命令。
/// </summary>
/// <param name="runtime">审批运行时的唯一写入入口。</param>
/// <param name="organization">可信组织查询。</param>
/// <param name="repository">核对人员最新在职状态。</param>
/// <param name="factory">读取流程管理员和剩余任务数的数据库上下文工厂。</param>
public sealed class DepartureJobHandler(IApprovalRuntimeAppService runtime,
    IOrganizationLookupAppService organization, IOrganizationRepository repository,
    IDbContextFactory<ApprovalDbContext> factory) : IScheduledJobHandler
{
    /// <summary>
    /// 离职自动改派任务类型。
    /// </summary>
    public const string JobKind = "departureReassign";

    /// <inheritdoc />
    public string Kind => JobKind;

    /// <inheritdoc />
    public async Task ExecuteAsync(ScheduledJobEntity job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var payload = JsonSerializer.Deserialize<DepartureJobPayload>(job.PayloadJson);
        if (string.IsNullOrWhiteSpace(payload?.PersonId))
        {
            throw new InvalidOperationException("离职任务缺少人员 ID。");
        }

        var person = await repository.GetPersonAsync(payload.PersonId, cancellationToken)
            ?? throw new InvalidOperationException("离职任务对应的组织人员不存在。");
        if (person.IsActive)
        {
            // 到执行时已经复职，不能再转走该人员当前的待办。
            return;
        }

        var candidates = await runtime.ListDepartureTasksAsync(person.Id, cancellationToken);
        foreach (var candidate in candidates)
        {
            var instance = await LoadInstanceContextAsync(candidate.InstanceId, cancellationToken);
            if (instance is null)
            {
                continue;
            }

            var snapshot = await organization.LoadAssigneeSnapshotAsync(instance.InitiatorId, cancellationToken);
            snapshot.InitiatingDepartmentId = instance.InitiatorDepartmentId ?? snapshot.InitiatingDepartmentId;
            snapshot.AdministratorId = await LoadAdministratorAsync(instance.FlowId, cancellationToken);
            var requestId = RequestId(job.Id, candidate.TaskId);
            try
            {
                await runtime.ReassignDepartedTaskAsync(candidate, person.Id, snapshot,
                    job.DueAt, requestId, cancellationToken);
            }
            catch (UserFriendlyException error) when (error.Code is ErrorCodes.Conflict or ErrorCodes.NotFound)
            {
                // 审批同时完成或他人接手时，重新扫描；仍归离职人员的任务让队列退避重试。
                var stillAssigned = await runtime.ListDepartureTasksAsync(person.Id, cancellationToken);
                if (stillAssigned.Any(item => item.InstanceId == candidate.InstanceId && item.TaskId == candidate.TaskId))
                {
                    throw;
                }
            }
        }

        await RefreshNoticeAsync(person.Id, cancellationToken);
    }

    /// <summary>
    /// 读取审批单快照；候选过期时返回空。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>审批单元数据。</returns>
    private async Task<InstanceEntity?> LoadInstanceContextAsync(string instanceId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Instances.AsNoTracking().SingleOrDefaultAsync(item => item.Id == instanceId,
            cancellationToken);
    }

    /// <summary>
    /// 按流程读取当前管理员；节点离职策略由引擎中已发布版本决定。
    /// </summary>
    /// <param name="flowId">流程 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>管理员内部人员 ID。</returns>
    private async Task<string?> LoadAdministratorAsync(string flowId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Flows.AsNoTracking().Where(item => item.Id == flowId)
            .Select(item => item.AdministratorId).SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 用持久任务 ID 和任务 ID 派生在单实例内稳定且短的幂等请求 ID。
    /// </summary>
    /// <param name="jobId">持久任务 ID。</param>
    /// <param name="taskId">审批任务 ID。</param>
    /// <returns>32 位十六进制请求 ID。</returns>
    private static string RequestId(string jobId, string taskId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jobId + ":" + taskId)))[..32].ToLowerInvariant();

    /// <summary>
    /// 成功扫描后把管理员提醒里的剩余在途数量更新为当前值。
    /// </summary>
    /// <param name="personId">离职人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    private async Task RefreshNoticeAsync(string personId, CancellationToken cancellationToken)
    {
        var remaining = await runtime.ListDepartureTasksAsync(personId, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var notice = await db.DepartureNotices.SingleOrDefaultAsync(item => item.PersonId == personId,
            cancellationToken);
        if (notice is not null)
        {
            notice.PendingTaskCount = remaining.Count;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// 持久离职任务只携带可信内部人员 ID，重试时重新读取最新组织与流程管理员。
/// </summary>
internal sealed class DepartureJobPayload
{
    /// <summary>
    /// 已由可信来源停用的内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;
}
