using System.Text.Json;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Organization;

/// <summary>
/// 将可信离职变更、管理员提醒和可恢复的改派任务放入同一组织事务。
/// </summary>
internal static class DepartureScheduleWriter
{
    /// <summary>
    /// 在当前组织事务中增加离职提醒与待执行任务。
    /// </summary>
    /// <param name="db">组织仓储正在使用的上下文。</param>
    /// <param name="personId">刚变为停用的内部人员 ID。</param>
    /// <param name="occurredAt">变更时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public static async Task AddAsync(ApprovalDbContext db, string personId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var pending = await (from task in db.ApprovalTasks
                             join instance in db.Instances on task.InstanceId equals instance.Id
                             where task.AssigneeId == personId &&
                                 instance.Status == EngineInstanceStatuses.Running &&
                                 (task.Status == EngineTaskStatuses.Pending || task.Status == EngineTaskStatuses.Queued)
                             select task.Id).CountAsync(cancellationToken);
        var notice = await db.DepartureNotices.SingleOrDefaultAsync(item => item.PersonId == personId,
            cancellationToken);
        if (notice is null)
        {
            db.DepartureNotices.Add(new DepartureNoticeEntity
            {
                PersonId = personId,
                CreatedAt = occurredAt,
                PendingTaskCount = pending,
            });
        }
        else
        {
            notice.CreatedAt = occurredAt;
            notice.PendingTaskCount = pending;
            notice.AcknowledgedAt = null;
        }

        // 任务与人员停用在同一次 SaveChanges 和事务中提交，服务重启也不会遗漏处理。
        db.ScheduledJobs.Add(new ScheduledJobEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = DepartureJobHandler.JobKind,
            PayloadJson = JsonSerializer.Serialize(new DepartureJobPayload { PersonId = personId }),
            Status = JobStatuses.Pending,
            DueAt = occurredAt,
        });
    }
}
