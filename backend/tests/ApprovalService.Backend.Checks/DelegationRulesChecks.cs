using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 委托规则的 UTC 时间边界及流程范围场景。
/// </summary>
internal static class DelegationRulesChecks
{
    /// <summary>
    /// 验证重叠、相接、独立流程与非法输入。
    /// </summary>
    /// <param name="check">检查记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var start = new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
        var existing = Rule("one", "owner", "proxy", start, start.AddHours(2), "FLOW_A");
        check("委托半开区间相接不冲突", Valid(Rule("two", "owner", "other", start.AddHours(2),
            start.AddHours(3), "FLOW_A"), existing));
        check("委托不同流程同时生效不冲突", Valid(Rule("two", "owner", "other", start.AddHours(1),
            start.AddHours(3), "FLOW_B"), existing));
        check("委托同一流程同时生效冲突", Fails(ErrorCodes.Conflict, Rule("two", "owner", "other",
            start.AddHours(1), start.AddHours(3), "FLOW_A"), existing));
        check("委托全部流程与指定流程冲突", Fails(ErrorCodes.Conflict, Rule("two", "owner", "other",
            start.AddHours(1), start.AddHours(3)), existing));
        check("委托不同人员范围独立", Valid(Rule("two", "other-owner", "other",
            start.AddHours(1), start.AddHours(3)), existing));
        check("委托不能指定本人", Fails(ErrorCodes.ValidationFailed,
            Rule("two", "owner", "owner", start, start.AddHours(1)), existing));
        check("委托结束必须晚于开始", Fails(ErrorCodes.ValidationFailed,
            Rule("two", "owner", "other", start, start), existing));
        check("委托必须传 UTC 时间", Fails(ErrorCodes.ValidationFailed,
            Rule("two", "owner", "other", start.ToOffset(TimeSpan.FromHours(8)), start.AddHours(1)), existing));
        check("委托流程范围不能重复", Fails(ErrorCodes.ValidationFailed,
            Rule("two", "owner", "other", start, start.AddHours(1), "FLOW_A", "FLOW_A"), existing));
    }

    /// <summary>
    /// 建立一个独立的委托场景输入。
    /// </summary>
    /// <param name="id">规则 ID。</param>
    /// <param name="principal">委托人。</param>
    /// <param name="proxy">代理人。</param>
    /// <param name="starts">开始时间。</param>
    /// <param name="ends">结束时间。</param>
    /// <param name="flows">限定流程。</param>
    /// <returns>待检查规则。</returns>
    private static DelegationRuleEntity Rule(string id, string principal, string proxy, DateTimeOffset starts,
        DateTimeOffset ends, params string[] flows) => new()
        {
            Id = id,
            PrincipalId = principal,
            DelegateId = proxy,
            StartsAtUtc = starts,
            EndsAtUtc = ends,
            FlowCodes = [.. flows],
        };

    /// <summary>
    /// 判断场景是否可以保存。
    /// </summary>
    /// <param name="candidate">待保存规则。</param>
    /// <param name="existing">现有规则。</param>
    /// <returns>校验通过时为真。</returns>
    private static bool Valid(DelegationRuleEntity candidate, params DelegationRuleEntity[] existing)
    {
        DelegationRules.Validate(candidate, existing);
        return true;
    }

    /// <summary>
    /// 判断失败场景的错误码。
    /// </summary>
    /// <param name="code">预期错误码。</param>
    /// <param name="candidate">待保存规则。</param>
    /// <param name="existing">现有规则。</param>
    /// <returns>收到预期业务错误时为真。</returns>
    private static bool Fails(string code, DelegationRuleEntity candidate, params DelegationRuleEntity[] existing)
    {
        try
        {
            DelegationRules.Validate(candidate, existing);
            return false;
        }
        catch (UserFriendlyException error)
        {
            return error.Code == code;
        }
    }
}
