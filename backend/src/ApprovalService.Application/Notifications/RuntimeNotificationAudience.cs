using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 从审批单当前权限与在职组织人员中选出本次可接收提醒的人。
/// </summary>
public static class RuntimeNotificationAudience
{
    /// <summary>
    /// 为本次命令的候选收件人核对审批单可见性、在职状态并取得邮箱。
    /// </summary>
    /// <param name="organization">可信组织人员仓储。</param>
    /// <param name="snapshot">命令前持久化的审批单快照。</param>
    /// <param name="instance">提交后的审批单。</param>
    /// <param name="graph">发起时锁定的执行图。</param>
    /// <param name="result">引擎命令结果。</param>
    /// <param name="newParticipants">本次命令实际产生的参与人。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>具备查看权限的在职人员和邮件地址。</returns>
    public static async Task<RuntimeNotificationAudienceResult> ResolveAsync(IOrganizationRepository organization,
        RuntimeSnapshot? snapshot, InstanceEntity instance, FlowExecutionGraph graph, EngineResult result,
        IReadOnlyList<InstanceParticipantEntity> newParticipants, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        var allowed = new HashSet<string>(StringComparer.Ordinal) { instance.InitiatorId };
        if (!string.IsNullOrWhiteSpace(instance.SubmittedById))
        {
            allowed.Add(instance.SubmittedById);
        }

        foreach (var person in (snapshot?.Participants ?? []).Concat(newParticipants))
        {
            allowed.Add(person.PersonId);
        }

        foreach (var task in result.State.Tasks.Where(task =>
            !graph.IsConfidential || task.Status == EngineTaskStatuses.Pending))
        {
            allowed.Add(task.AssigneeId);
            if (graph.IsConfidential && task.Status == EngineTaskStatuses.Pending &&
                !string.IsNullOrWhiteSpace(task.DelegatorId))
            {
                allowed.Add(task.DelegatorId);
            }
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal)
        {
            instance.InitiatorId,
        };
        if (!string.IsNullOrWhiteSpace(instance.SubmittedById))
        {
            candidates.Add(instance.SubmittedById);
        }

        foreach (var task in result.State.Tasks.Where(task => task.Status == EngineTaskStatuses.Pending))
        {
            candidates.Add(task.AssigneeId);
            if (!string.IsNullOrWhiteSpace(task.DelegatorId))
            {
                candidates.Add(task.DelegatorId);
            }
        }

        foreach (var item in result.Events)
        {
            foreach (var personId in item.ResolvedAssigneeIds)
            {
                candidates.Add(personId);
            }

            if (!string.IsNullOrWhiteSpace(item.Audit?.TargetUserId))
            {
                candidates.Add(item.Audit.TargetUserId);
            }

            foreach (var personId in item.Audit?.MentionedUserIds ?? [])
            {
                candidates.Add(personId);
            }
        }

        // 只检查本次会收到消息的人，避免在长流程中逐一读取全部历史参与人。
        var authorized = new HashSet<string>(StringComparer.Ordinal);
        var emails = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var personId in candidates.Where(allowed.Contains).OrderBy(id => id, StringComparer.Ordinal))
        {
            var person = await organization.GetPersonAsync(personId, cancellationToken);
            if (person?.IsActive != true)
            {
                continue;
            }

            authorized.Add(personId);
            emails.Add(personId, person.Email);
        }

        return new RuntimeNotificationAudienceResult(authorized, emails);
    }
}

/// <summary>
/// 本次审批通知可用的人员授权与邮箱快照。
/// </summary>
/// <param name="AuthorizedIds">可查看此审批单的在职人员。</param>
/// <param name="EmailAddresses">上述人员的邮箱；可为空。</param>
public sealed record RuntimeNotificationAudienceResult(IReadOnlySet<string> AuthorizedIds,
    IReadOnlyDictionary<string, string?> EmailAddresses);
