using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Domain.Organization;

/// <summary>
/// 角色管理统一输入规则。
/// </summary>
public static class RoleManagementRules
{
    /// <summary>
    /// 组织管理员的稳定编码。
    /// </summary>
    public const string OrganizationAdmin = "organization-admin";
    /// <summary>
    /// 流程管理员的稳定编码。
    /// </summary>
    public const string FlowAdmin = "flow-admin";

    /// <summary>
    /// 判断不可删除的系统角色。
    /// </summary>
    /// <param name="code">角色编码。</param>
    /// <returns>是否为内置角色。</returns>
    public static bool IsBuiltIn(string code) => string.Equals(code, OrganizationAdmin, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(code, FlowAdmin, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 校验编码，保留历史编码的大小写语义。
    /// </summary>
    /// <param name="code">待校验编码。</param>
    /// <returns>原编码。</returns>
    public static string Code(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128 ||
            code.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "角色编码须为 1–128 位字母、数字、连字符、下划线或点。");
        }
        // SQL Server 常用不区分大小写排序规则，内置编码必须统一后再执行最后管理员保护。
        return string.Equals(code, OrganizationAdmin, StringComparison.OrdinalIgnoreCase) ? OrganizationAdmin :
            string.Equals(code, FlowAdmin, StringComparison.OrdinalIgnoreCase) ? FlowAdmin : code;
    }

    /// <summary>
    /// 校验名称或操作原因。
    /// </summary>
    /// <param name="value">原始文字。</param>
    /// <param name="reason">是否校验原因。</param>
    /// <returns>去除首尾空白的文字。</returns>
    public static string Text(string value, bool reason)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length < (reason ? 2 : 1) || value.Length > (reason ? 200 : 256) || value.Any(char.IsControl))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                reason ? "操作原因须为 2–200 个字，且不能换行。" : "角色名称须为 1–256 个字，且不能换行。");
        }
        return value;
    }

    /// <summary>
    /// 校验完整成员列表，拒绝重复或空白人员。
    /// </summary>
    /// <param name="members">目标成员。</param>
    /// <returns>稳定排序的成员。</returns>
    public static IReadOnlyList<string> Members(IReadOnlyList<string> members)
    {
        if (members is null || members.Count > 10000 ||
            members.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(char.IsControl)) ||
            members.Distinct(StringComparer.Ordinal).Count() != members.Count)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "角色成员须为不重复的有效内部人员 ID，最多 10000 人。");
        }
        return members.Order(StringComparer.Ordinal).ToArray();
    }
}
