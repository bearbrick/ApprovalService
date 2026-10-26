using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Domain.Center;

/// <summary>
/// 检查委托规则有效期与流程范围，阻止同一委托人同时出现多个代理人。
/// </summary>
public static class DelegationRules
{
    /// <summary>
    /// 检查待保存规则及其与已保存规则的冲突。
    /// </summary>
    /// <param name="candidate">待保存的委托规则。</param>
    /// <param name="existing">同一委托人的已保存规则。</param>
    public static void Validate(DelegationRuleEntity candidate, IReadOnlyCollection<DelegationRuleEntity> existing)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existing);
        if (string.IsNullOrWhiteSpace(candidate.Id) || string.IsNullOrWhiteSpace(candidate.PrincipalId) ||
            string.IsNullOrWhiteSpace(candidate.DelegateId) || candidate.PrincipalId == candidate.DelegateId ||
            candidate.StartsAtUtc.Offset != TimeSpan.Zero || candidate.EndsAtUtc.Offset != TimeSpan.Zero ||
            candidate.StartsAtUtc >= candidate.EndsAtUtc || candidate.FlowCodes is null ||
            candidate.FlowCodes.Any(string.IsNullOrWhiteSpace) ||
            candidate.FlowCodes.Distinct(StringComparer.Ordinal).Count() != candidate.FlowCodes.Count)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "委托人、代理人、UTC 有效期或流程范围不正确。");
        }

        foreach (var rule in existing)
        {
            // 同一标识是更新自身；端点相接时两个左闭右开区间没有交集。
            if (rule.Id == candidate.Id || rule.PrincipalId != candidate.PrincipalId ||
                candidate.EndsAtUtc <= rule.StartsAtUtc || rule.EndsAtUtc <= candidate.StartsAtUtc)
            {
                continue;
            }

            // 空范围代表全部流程；两个限定范围只有交集存在时才冲突。
            if (candidate.FlowCodes.Count == 0 || rule.FlowCodes.Count == 0 ||
                candidate.FlowCodes.Intersect(rule.FlowCodes, StringComparer.Ordinal).Any())
            {
                throw new UserFriendlyException(ErrorCodes.Conflict, "同一委托人的委托时间与流程范围不能重叠。");
            }
        }
    }
}
