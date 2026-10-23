using System.Globalization;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 用调用方分配的时间和流水号生成审批编号；本类不负责持久化或抢号。
/// </summary>
public static class ApprovalSerialNumber
{
    /// <summary>
    /// 生成审批编号，例如 SP-202610-0007。
    /// </summary>
    /// <param name="prefix">一至十六位的字母、数字、下划线或短横线。</param>
    /// <param name="datePattern">日期格式：yyyy、yyyyMM 或 yyyyMMdd。</param>
    /// <param name="digits">流水号位数，一至十二位。</param>
    /// <param name="sequence">调用方在该前缀和日期桶内分配的正流水号。</param>
    /// <param name="occurredAt">调用方提供的带时区时间。</param>
    /// <returns>带前缀、日期和补零流水号的编号。</returns>
    public static string Format(string prefix, string datePattern, int digits, long sequence, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 16
            || !prefix.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批编号前缀只能包含一至十六位字母、数字、下划线或短横线。");
        }

        if (datePattern is not ("yyyy" or "yyyyMM" or "yyyyMMdd"))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批编号日期格式只能是 yyyy、yyyyMM 或 yyyyMMdd。");
        }

        if (digits is < 1 or > 12 || sequence < 1 || sequence.ToString(CultureInfo.InvariantCulture).Length > digits)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批编号流水号必须为正数且不能超过配置位数。");
        }

        var date = occurredAt.ToString(datePattern, CultureInfo.InvariantCulture);
        return $"{prefix}-{date}-{sequence.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0')}";
    }
}
