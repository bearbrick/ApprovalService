using ApprovalService.Domain.Organization;

namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 将历史流程管理员标识保守解析为唯一的内部人员 ID。
/// </summary>
public static class FlowAdministratorIdentityRules
{
    /// <summary>
    /// 同时按内部 ID 和工号查找人员，只有唯一人员命中才可自动修正。
    /// </summary>
    /// <param name="legacyValue">历史标识。</param>
    /// <param name="people">当前人员主数据。</param>
    /// <returns>匹配人员的内部 ID 与去重后的匹配数量。</returns>
    public static (string? PersonId, int MatchCount) Resolve(string legacyValue,
        IReadOnlyCollection<PersonEntity> people)
    {
        ArgumentNullException.ThrowIfNull(legacyValue);
        ArgumentNullException.ThrowIfNull(people);
        var matches = people.Where(person =>
                string.Equals(person.Id, legacyValue, StringComparison.Ordinal) ||
                string.Equals(person.EmployeeNumber, legacyValue, StringComparison.Ordinal))
            .Select(person => person.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return matches.Length == 1 ? (matches[0], 1) : (null, matches.Length);
    }
}
