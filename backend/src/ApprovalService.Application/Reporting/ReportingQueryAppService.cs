using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Reporting;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Reporting;

/// <summary>
/// 内部组织管理员报表查询；保密审批只提供匿名数量。
/// </summary>
/// <param name="organization">当前组织和角色权限。</param>
/// <param name="factsRepository">跨期最小化事实读取。</param>
/// <param name="timeProvider">服务端观察时间。</param>
public sealed class ReportingQueryAppService(IOrganizationRepository organization,
    IReportingFactsRepository factsRepository, TimeProvider timeProvider)
{
    /// <summary>
    /// 核对本地会话和当前组织管理员角色后计算内部报表。
    /// </summary>
    /// <param name="input">时间、流程、来源和部门筛选。</param>
    /// <param name="actorId">已验证令牌中的内部人员 ID。</param>
    /// <param name="sessionKind">已验证令牌中的会话来源。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仅包含已授权非保密指标和匿名保密数量的结果。</returns>
    public async Task<ReportingQueryDto> GetAsync(ReportingQueryInput input, string actorId,
        string sessionKind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
        }

        if (sessionKind != IdentitySessionKinds.Local ||
            !await organization.IsOrganizationAdminAsync(actorId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "仅本地登录的组织管理员可查看报表。");
        }

        var asOfUtc = timeProvider.GetUtcNow();
        if (input.FromUtc.Offset != TimeSpan.Zero || input.UntilUtc.Offset != TimeSpan.Zero ||
            input.FromUtc >= input.UntilUtc)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "报表时间范围必须是非空的 UTC 半开区间。");
        }

        var query = new ReportingMetricsQuery
        {
            FromUtc = input.FromUtc,
            UntilUtc = input.UntilUtc,
            AsOfUtc = asOfUtc,
            FlowCode = input.FlowCode,
            ClientAppCode = input.ClientAppCode,
            InitiatorDepartmentId = input.InitiatorDepartmentId,
        };
        var facts = await factsRepository.ReadForMetricsAsync(query, cancellationToken);
        var visibleInstances = facts.Instances.Where(item => !item.IsConfidential).ToArray();
        var confidentialInstances = facts.Instances.Where(item => item.IsConfidential).ToArray();
        var visibleIds = visibleInstances.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var visibleTasks = facts.Tasks.Where(item => visibleIds.Contains(item.InstanceId)).ToArray();
        var visibleTaskIds = visibleTasks.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        // 保密实例的任务和事件不进入指标计算，更不会出现在返回模型中。
        var visibleFacts = new ReportingFacts
        {
            Instances = visibleInstances,
            Tasks = visibleTasks,
            TaskCreatedEvents = facts.TaskCreatedEvents.Where(item => visibleIds.Contains(item.InstanceId)).ToArray(),
            TaskActivatedEvents = facts.TaskActivatedEvents.Where(item => visibleTaskIds.Contains(item.TaskId)).ToArray(),
        };
        return new ReportingQueryDto
        {
            VisibleMetrics = ReportingMetricsCalculator.Calculate(visibleFacts, query),
            ConfidentialStartedCount = confidentialInstances.Count(item =>
                item.StartedAt >= query.FromUtc && item.StartedAt < query.UntilUtc),
            ConfidentialCompletedCount = confidentialInstances.Count(item =>
                item.EndedAt >= query.FromUtc && item.EndedAt < query.UntilUtc),
        };
    }
}
