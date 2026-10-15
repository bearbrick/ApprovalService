namespace ApprovalService.Engine.Expressions;

/// <summary>
/// 将金额转换成人民币财务大写，用于只读详情和打印。
/// </summary>
public static class RmbUppercase
{
    /// <summary>
    /// 大写数字。
    /// </summary>
    private static readonly string[] Digits = ["零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖"];

    /// <summary>
    /// 四位组内单位。
    /// </summary>
    private static readonly string[] SmallUnits = ["", "拾", "佰", "仟"];

    /// <summary>
    /// 四位组间单位。
    /// </summary>
    private static readonly string[] LargeUnits = ["", "万", "亿", "兆"];

    /// <summary>
    /// 将金额四舍五入到分并转成财务大写；最大支持 9999 兆。
    /// </summary>
    /// <param name="amount">金额。</param>
    /// <returns>人民币大写。</returns>
    public static string Format(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) >= 10000000000000000m)
        {
            throw new ApprovalExpressionException("金额超出人民币大写支持范围。");
        }

        var negative = rounded < 0;
        var absolute = Math.Abs(rounded);
        var yuan = decimal.Truncate(absolute);
        var cents = (int)((absolute - yuan) * 100m);
        var result = IntegerPart(yuan) + "元";
        if (cents == 0)
        {
            result += "整";
        }
        else
        {
            var jiao = cents / 10;
            var fen = cents % 10;
            if (jiao > 0)
            {
                result += Digits[jiao] + "角";
            }
            else if (yuan > 0)
            {
                result += "零";
            }

            result += fen > 0 ? Digits[fen] + "分" : "整";
        }

        return negative ? "负" + result : result;
    }

    /// <summary>
    /// 转换元的整数部分，处理万、亿组之间的零。
    /// </summary>
    /// <param name="yuan">整数元。</param>
    /// <returns>中文大写数字。</returns>
    private static string IntegerPart(decimal yuan)
    {
        if (yuan == 0)
        {
            return Digits[0];
        }

        var groups = new int[LargeUnits.Length];
        for (var index = 0; index < groups.Length && yuan > 0; index++)
        {
            groups[index] = (int)(yuan % 10000m);
            yuan = decimal.Truncate(yuan / 10000m);
        }

        var result = string.Empty;
        var pendingZero = false;
        for (var index = groups.Length - 1; index >= 0; index--)
        {
            var group = groups[index];
            if (group == 0)
            {
                pendingZero = result.Length > 0;
                continue;
            }

            if (result.Length > 0 && (pendingZero || group < 1000))
            {
                result += Digits[0];
            }

            result += Group(group) + LargeUnits[index];
            pendingZero = false;
        }

        return result;
    }

    /// <summary>
    /// 将一至四位的数字转为中文大写。
    /// </summary>
    /// <param name="number">四位以内的非零数。</param>
    /// <returns>中文大写数字。</returns>
    private static string Group(int number)
    {
        var result = string.Empty;
        var pendingZero = false;
        for (var position = 3; position >= 0; position--)
        {
            var divisor = (int)Math.Pow(10, position);
            var digit = number / divisor % 10;
            if (digit == 0)
            {
                pendingZero = result.Length > 0;
                continue;
            }

            if (pendingZero)
            {
                result += Digits[0];
            }

            result += Digits[digit] + SmallUnits[position];
            pendingZero = false;
        }

        return result;
    }
}
