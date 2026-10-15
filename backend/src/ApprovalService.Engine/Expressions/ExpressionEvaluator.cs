using System.Globalization;
using System.Text.Json;

namespace ApprovalService.Engine.Expressions;

/// <summary>
/// 对已解析的语法树执行无副作用求值。
/// </summary>
internal static class ExpressionEvaluator
{
    /// <summary>
    /// 单次聚合允许的最多元素数。
    /// </summary>
    private const int MaxItems = 1000;

    /// <summary>
    /// 计算一个语法树节点。
    /// </summary>
    /// <param name="node">语法树节点。</param>
    /// <param name="data">业务数据快照。</param>
    /// <param name="context">发起人等上下文。</param>
    /// <returns>计算结果。</returns>
    public static object? Evaluate(ExpressionNode node, JsonElement data, IReadOnlyDictionary<string, object?>? context)
    {
        return node.Kind switch
        {
            "literal" => node.Value,
            "field" => Resolve(node.Name, data, context),
            "unary" => EvaluateUnary(node, data, context),
            "binary" => EvaluateBinary(node, data, context),
            "call" => EvaluateCall(node, data, context),
            _ => throw new ApprovalExpressionException("表达式节点无效。"),
        };
    }

    /// <summary>
    /// 求一元负号或逻辑非。
    /// </summary>
    /// <param name="node">一元节点。</param>
    /// <param name="data">数据快照。</param>
    /// <param name="context">上下文。</param>
    /// <returns>一元计算结果。</returns>
    private static object EvaluateUnary(ExpressionNode node, JsonElement data, IReadOnlyDictionary<string, object?>? context)
    {
        var value = Evaluate(node.Children[0], data, context);
        try
        {
            return node.Name == "-" ? -Number(value) : !Boolean(value);
        }
        catch (OverflowException)
        {
            throw new ApprovalExpressionException("数字计算超出范围。");
        }
    }

    /// <summary>
    /// 求二元运算；且、或只计算需要的一侧。
    /// </summary>
    /// <param name="node">二元节点。</param>
    /// <param name="data">数据快照。</param>
    /// <param name="context">上下文。</param>
    /// <returns>二元计算结果。</returns>
    private static object EvaluateBinary(ExpressionNode node, JsonElement data, IReadOnlyDictionary<string, object?>? context)
    {
        var left = Evaluate(node.Children[0], data, context);
        if (node.Name is "AND" or "&&")
        {
            return Boolean(left) && Boolean(Evaluate(node.Children[1], data, context));
        }

        if (node.Name is "OR" or "||")
        {
            return Boolean(left) || Boolean(Evaluate(node.Children[1], data, context));
        }

        var right = Evaluate(node.Children[1], data, context);
        try
        {
            return node.Name switch
            {
                "+" => checked(Number(left) + Number(right)),
                "-" => checked(Number(left) - Number(right)),
                "*" => checked(Number(left) * Number(right)),
                "/" => Number(right) == 0 ? throw new ApprovalExpressionException("除数不能为零。") : Number(left) / Number(right),
                "==" => Compare(left, right) == 0,
                "!=" => Compare(left, right) != 0,
                ">" => Compare(left, right) > 0,
                "<" => Compare(left, right) < 0,
                ">=" => Compare(left, right) >= 0,
                "<=" => Compare(left, right) <= 0,
                _ => throw new ApprovalExpressionException("不支持的运算符。"),
            };
        }
        catch (OverflowException)
        {
            throw new ApprovalExpressionException("数字计算超出范围。");
        }
    }

    /// <summary>
    /// 执行白名单函数；IF 对未选中分支不求值。
    /// </summary>
    /// <param name="node">函数节点。</param>
    /// <param name="data">数据快照。</param>
    /// <param name="context">上下文。</param>
    /// <returns>函数计算结果。</returns>
    private static object? EvaluateCall(ExpressionNode node, JsonElement data, IReadOnlyDictionary<string, object?>? context)
    {
        if (node.Name == "IF")
        {
            RequireCount(node, 3);
            return Evaluate(node.Children[Boolean(Evaluate(node.Children[0], data, context)) ? 1 : 2], data, context);
        }

        if (node.Name == "DATEDIFF")
        {
            RequireCount(node, 2);
            var start = Date(Evaluate(node.Children[0], data, context));
            var end = Date(Evaluate(node.Children[1], data, context));
            return (decimal)(end.DayNumber - start.DayNumber);
        }

        if (node.Name == "RMBUPPER")
        {
            RequireCount(node, 1);
            return RmbUppercase.Format(Number(Evaluate(node.Children[0], data, context)));
        }

        if (node.Name == "ANY")
        {
            if (node.Children.Count is < 1 or > 2)
            {
                throw new ApprovalExpressionException("ANY 只接受一或两个参数。");
            }

            var values = Expand(Evaluate(node.Children[0], data, context));
            if (node.Children.Count == 1)
            {
                return values.Any(Boolean);
            }

            var target = Evaluate(node.Children[1], data, context);
            return values.Any(value => Compare(value, target) == 0);
        }

        if (node.Children.Count == 0)
        {
            throw new ApprovalExpressionException($"{node.Name} 至少需要一个参数。");
        }

        var numbers = node.Children.SelectMany(child => Expand(Evaluate(child, data, context)))
            .Take(MaxItems + 1).Select(Number).ToArray();
        if (numbers.Length > MaxItems)
        {
            throw new ApprovalExpressionException("聚合元素超过上限。");
        }
        if (numbers.Length == 0)
        {
            throw new ApprovalExpressionException($"{node.Name} 不能计算空集合。");
        }

        try
        {
            return node.Name switch
            {
                "SUM" => numbers.Aggregate(0m, (sum, number) => checked(sum + number)),
                "AVG" => numbers.Aggregate(0m, (sum, number) => checked(sum + number)) / numbers.Length,
                "MAX" => numbers.Max(),
                "MIN" => numbers.Min(),
                _ => throw new ApprovalExpressionException($"不支持函数 {node.Name}。"),
            };
        }
        catch (OverflowException)
        {
            throw new ApprovalExpressionException("数字计算超出范围。");
        }
    }

    /// <summary>
    /// 从数据快照或上下文按点分路径读取；明细列返回值集合。
    /// </summary>
    /// <param name="path">字段路径。</param>
    /// <param name="data">数据快照。</param>
    /// <param name="context">上下文。</param>
    /// <returns>字段值。</returns>
    private static object? Resolve(string path, JsonElement data, IReadOnlyDictionary<string, object?>? context)
    {
        if (path.StartsWith("initiator.", StringComparison.Ordinal))
        {
            return context is not null && context.TryGetValue(path, out var value)
                ? NormalizeContext(value) : throw new ApprovalExpressionException($"缺少上下文字段 {path}。");
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new ApprovalExpressionException($"缺少审批字段 {path}。");
        }

        // 完整 key 优先，避免把合法的顶层 key 中的点号误认成明细分隔符。
        if (data.TryGetProperty(path, out var direct))
        {
            return ConvertValue(direct);
        }

        // 明细表的 key 和子字段 key 都可能含点号；只在数组型顶层 key 中选最长前缀。
        JsonProperty? table = null;
        var hasPrefix = false;
        foreach (var property in data.EnumerateObject())
        {
            if (!path.StartsWith(property.Name + ".", StringComparison.Ordinal))
            {
                continue;
            }

            hasPrefix = true;
            if (property.Value.ValueKind == JsonValueKind.Array
                && (!table.HasValue || property.Name.Length > table.Value.Name.Length))
            {
                table = property;
            }
        }

        if (!table.HasValue)
        {
            throw new ApprovalExpressionException(hasPrefix
                ? $"审批字段 {path} 不是明细列。" : $"缺少审批字段 {path}。");
        }

        var childKey = path[(table.Value.Name.Length + 1)..];
        var results = new List<object?>();
        foreach (var item in table.Value.Value.EnumerateArray())
        {
            if (results.Count == MaxItems)
            {
                throw new ApprovalExpressionException("明细行超过表达式计算上限。");
            }

            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(childKey, out var cell))
            {
                throw new ApprovalExpressionException($"明细字段 {path} 缺失。");
            }

            results.Add(ConvertValue(cell));
        }

        return results;
    }

    /// <summary>
    /// 将组织上下文中的常见整数或浮点数规范化为十进制数。
    /// </summary>
    /// <param name="value">调用方提供的上下文值。</param>
    /// <returns>受限求值类型。</returns>
    private static object? NormalizeContext(object? value)
    {
        try
        {
            return value switch
            {
                byte or short or int or long or float or double => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                decimal or bool or string or null => value,
                _ => throw new ApprovalExpressionException("上下文字段类型不受支持。"),
            };
        }
        catch (OverflowException)
        {
            throw new ApprovalExpressionException("上下文数字超出范围。");
        }
    }

    /// <summary>
    /// 把 JSON 标量转成受限求值类型。
    /// </summary>
    /// <param name="value">JSON 值。</param>
    /// <returns>标量值。</returns>
    private static object? ConvertValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new ApprovalExpressionException("表达式引用的字段必须是标量。"),
    };

    /// <summary>
    /// 将标量或集合展开为受限序列。
    /// </summary>
    /// <param name="value">表达式值。</param>
    /// <returns>受限序列。</returns>
    private static IEnumerable<object?> Expand(object? value)
    {
        if (value is List<object?> items)
        {
            if (items.Count > MaxItems)
            {
                throw new ApprovalExpressionException("聚合元素超过上限。");
            }

            return items;
        }

        return [value];
    }

    /// <summary>
    /// 将表达式值读成十进制数，不做隐式字符串转换。
    /// </summary>
    /// <param name="value">表达式值。</param>
    /// <returns>十进制数。</returns>
    private static decimal Number(object? value) => value is decimal number
        ? number : throw new ApprovalExpressionException("表达式需要数字字段。");

    /// <summary>
    /// 将表达式值读成布尔值。
    /// </summary>
    /// <param name="value">表达式值。</param>
    /// <returns>布尔值。</returns>
    private static bool Boolean(object? value) => value is bool result
        ? result : throw new ApprovalExpressionException("表达式需要布尔值。");

    /// <summary>
    /// 将 ISO 日期字符串读成日期。
    /// </summary>
    /// <param name="value">表达式值。</param>
    /// <returns>日期。</returns>
    private static DateOnly Date(object? value) => value is string text
        && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
        ? result : throw new ApprovalExpressionException("日期应使用 yyyy-MM-dd 格式。");

    /// <summary>
    /// 在相同类型间比较，禁止不可靠的隐式转换。
    /// </summary>
    /// <param name="left">左值。</param>
    /// <param name="right">右值。</param>
    /// <returns>比较结果。</returns>
    private static int Compare(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null ? 0 : left is null ? -1 : 1;
        }

        if (left is decimal a && right is decimal b)
        {
            return a.CompareTo(b);
        }

        if (left is string x && right is string y)
        {
            return string.CompareOrdinal(x, y);
        }

        if (left is bool p && right is bool q)
        {
            return p.CompareTo(q);
        }

        throw new ApprovalExpressionException("比较的两个值类型不一致。");
    }

    /// <summary>
    /// 核对固定参数数量。
    /// </summary>
    /// <param name="node">函数节点。</param>
    /// <param name="count">所需参数数。</param>
    private static void RequireCount(ExpressionNode node, int count)
    {
        if (node.Children.Count != count)
        {
            throw new ApprovalExpressionException($"{node.Name} 需要 {count} 个参数。");
        }
    }
}
