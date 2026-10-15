using System.Globalization;
using System.Text.Json;

namespace ApprovalService.Engine.Expressions;

/// <summary>
/// 对审批快照求值的受限表达式；解析时拒绝超长、深嵌套和未知函数。
/// </summary>
public static class ApprovalExpression
{
    /// <summary>
    /// 表达式字符数上限。
    /// </summary>
    public const int MaxLength = 2048;

    /// <summary>
    /// 表达式嵌套层数上限。
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// 解析并求值；字段引用写作 ${field} 或 ${items.amount}。
    /// </summary>
    /// <param name="expression">受限表达式。</param>
    /// <param name="data">审批数据对象。</param>
    /// <param name="context">可选的 initiator 等上下文值。</param>
    /// <returns>数字、布尔值、字符串或空值。</returns>
    public static object? Evaluate(string expression, JsonElement data, IReadOnlyDictionary<string, object?>? context = null)
    {
        var parser = new ExpressionParser(expression);
        var syntax = parser.Parse();
        return ExpressionEvaluator.Evaluate(syntax, data, context);
    }

    /// <summary>
    /// 仅解析并校验语法与函数白名单，不访问数据。
    /// </summary>
    /// <param name="expression">受限表达式。</param>
    /// <returns>引用的字段路径，去重后按出现顺序排列。</returns>
    public static IReadOnlyList<string> Validate(string expression)
    {
        var parser = new ExpressionParser(expression);
        parser.Parse();
        return parser.References;
    }
}

/// <summary>
/// 表达式语法错误或求值错误。
/// </summary>
public sealed class ApprovalExpressionException : Exception
{
    /// <summary>
    /// 构造可展示的表达式错误。
    /// </summary>
    /// <param name="message">中文错误说明。</param>
    public ApprovalExpressionException(string message) : base(message)
    {
    }
}

/// <summary>
/// 表达式语法树节点。
/// </summary>
internal sealed class ExpressionNode
{
    /// <summary>
    /// 节点类别：literal、field、unary、binary 或 call。
    /// </summary>
    public required string Kind
    {
        get; init;
    }

    /// <summary>
    /// 运算符、函数名或字段路径。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 字面量值。
    /// </summary>
    public object? Value
    {
        get; init;
    }

    /// <summary>
    /// 子表达式。
    /// </summary>
    public IReadOnlyList<ExpressionNode> Children { get; init; } = [];
}

/// <summary>
/// 递归下降解析器；所有分支都有确定的字符和深度上限。
/// </summary>
internal sealed class ExpressionParser
{
    /// <summary>
    /// 函数白名单。
    /// </summary>
    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVG", "MAX", "MIN", "IF", "ANY", "DATEDIFF", "RMBUPPER",
    };

    /// <summary>
    /// 要解析的原文。
    /// </summary>
    private readonly string source;

    /// <summary>
    /// 当前字符位置。
    /// </summary>
    private int offset;

    /// <summary>
    /// 已消费的语法单元数。
    /// </summary>
    private int tokens;

    /// <summary>
    /// 记录字段引用。
    /// </summary>
    private readonly List<string> references = [];

    /// <summary>
    /// 建立解析器并检查原文长度。
    /// </summary>
    /// <param name="source">表达式原文。</param>
    public ExpressionParser(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > ApprovalExpression.MaxLength)
        {
            throw new ApprovalExpressionException("表达式为空或超过长度上限。");
        }

        this.source = source;
    }

    /// <summary>
    /// 字段引用列表。
    /// </summary>
    public IReadOnlyList<string> References => references;

    /// <summary>
    /// 解析完整输入。
    /// </summary>
    /// <returns>完整语法树。</returns>
    public ExpressionNode Parse()
    {
        var node = ParseExpression(0, 0);
        SkipSpace();
        if (offset != source.Length)
        {
            throw new ApprovalExpressionException($"表达式在第 {offset + 1} 个字符附近无效。");
        }

        return node;
    }

    /// <summary>
    /// 按运算符优先级解析二元表达式。
    /// </summary>
    /// <param name="minimum">允许的最低优先级。</param>
    /// <param name="depth">当前嵌套深度。</param>
    /// <returns>语法树节点。</returns>
    private ExpressionNode ParseExpression(int minimum, int depth)
    {
        if (depth > ApprovalExpression.MaxDepth)
        {
            throw new ApprovalExpressionException("表达式嵌套过深。");
        }

        var left = ParsePrimary(depth + 1);
        while (true)
        {
            SkipSpace();
            var (name, priority, width) = ReadOperator();
            if (priority < minimum || width == 0)
            {
                return left;
            }

            offset += width;
            CountToken();
            var right = ParseExpression(priority + 1, depth + 1);
            left = new ExpressionNode { Kind = "binary", Name = name, Children = [left, right] };
        }
    }

    /// <summary>
    /// 解析字面量、字段、函数调用、一元运算或括号。
    /// </summary>
    /// <param name="depth">当前嵌套深度。</param>
    /// <returns>语法树节点。</returns>
    private ExpressionNode ParsePrimary(int depth)
    {
        if (depth > ApprovalExpression.MaxDepth)
        {
            throw new ApprovalExpressionException("表达式嵌套过深。");
        }

        SkipSpace();
        CountToken();
        if (Take("("))
        {
            var inner = ParseExpression(0, depth + 1);
            Expect(")");
            return inner;
        }

        if (Take("-"))
        {
            return new ExpressionNode { Kind = "unary", Name = "-", Children = [ParsePrimary(depth + 1)] };
        }

        if (Take("!"))
        {
            return new ExpressionNode { Kind = "unary", Name = "!", Children = [ParsePrimary(depth + 1)] };
        }

        if (Take("${"))
        {
            var start = offset;
            while (offset < source.Length && source[offset] != '}')
            {
                offset++;
            }

            if (offset == source.Length)
            {
                throw new ApprovalExpressionException("字段引用缺少右花括号。");
            }

            var path = source[start..offset++];
            if (!IsPath(path))
            {
                throw new ApprovalExpressionException("字段引用路径无效。");
            }

            if (!references.Contains(path, StringComparer.Ordinal))
            {
                references.Add(path);
            }

            return new ExpressionNode { Kind = "field", Name = path };
        }

        if (offset < source.Length && (char.IsDigit(source[offset]) || source[offset] == '.'))
        {
            var start = offset;
            while (offset < source.Length && (char.IsDigit(source[offset]) || source[offset] == '.'))
            {
                offset++;
            }

            if (!decimal.TryParse(source[start..offset], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                throw new ApprovalExpressionException("数字字面量无效。");
            }

            return new ExpressionNode { Kind = "literal", Value = number };
        }

        if (offset < source.Length && (source[offset] == '\'' || source[offset] == '"'))
        {
            var quote = source[offset++];
            var start = offset;
            while (offset < source.Length && source[offset] != quote)
            {
                offset++;
            }

            if (offset == source.Length)
            {
                throw new ApprovalExpressionException("字符串缺少结束引号。");
            }

            var value = source[start..offset++];
            return new ExpressionNode { Kind = "literal", Value = value };
        }

        var wordStart = offset;
        while (offset < source.Length && char.IsLetter(source[offset]))
        {
            offset++;
        }

        var word = source[wordStart..offset];
        if (word.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || word.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new ExpressionNode { Kind = "literal", Value = word.Equals("TRUE", StringComparison.OrdinalIgnoreCase) };
        }

        if (!Functions.Contains(word))
        {
            throw new ApprovalExpressionException(word.Length == 0 ? "表达式缺少操作数。" : $"不支持函数 {word}。");
        }

        Expect("(");
        var arguments = new List<ExpressionNode>();
        SkipSpace();
        if (!Take(")"))
        {
            do
            {
                if (arguments.Count == 64)
                {
                    throw new ApprovalExpressionException("函数参数过多。");
                }

                arguments.Add(ParseExpression(0, depth + 1));
                SkipSpace();
                if (Take(")"))
                {
                    break;
                }

                Expect(",");
            }
            while (true);
        }

        return new ExpressionNode { Kind = "call", Name = word.ToUpperInvariant(), Children = arguments };
    }

    /// <summary>
    /// 读取当前位置的二元运算符。
    /// </summary>
    /// <returns>运算符、优先级和字符宽度。</returns>
    private (string Name, int Priority, int Width) ReadOperator()
    {
        foreach (var (symbol, priority) in new[]
        {
            ("||", 1), ("OR", 1), ("&&", 2), ("AND", 2), ("==", 3), ("!=", 3),
            (">=", 4), ("<=", 4), (">", 4), ("<", 4), ("+", 5), ("-", 5), ("*", 6), ("/", 6),
        })
        {
            if (source.AsSpan(offset).StartsWith(symbol, StringComparison.OrdinalIgnoreCase)
                && (symbol is not ("OR" or "AND") || offset + symbol.Length == source.Length || !char.IsLetter(source[offset + symbol.Length])))
            {
                return (symbol.ToUpperInvariant(), priority, symbol.Length);
            }
        }

        return (string.Empty, -1, 0);
    }

    /// <summary>
    /// 检查字段路径仅含安全标识符及点号。
    /// </summary>
    /// <param name="path">字段路径。</param>
    /// <returns>路径是否有效。</returns>
    private static bool IsPath(string path) => path.Split('.').All(part => part.Length is > 0 and <= 64
        && (char.IsLetter(part[0]) || part[0] == '_') && part.All(character => char.IsLetterOrDigit(character) || character == '_'));

    /// <summary>
    /// 消费指定文本。
    /// </summary>
    /// <param name="text">期望文本。</param>
    /// <returns>是否已消费。</returns>
    private bool Take(string text)
    {
        SkipSpace();
        if (!source.AsSpan(offset).StartsWith(text, StringComparison.Ordinal))
        {
            return false;
        }

        offset += text.Length;
        return true;
    }

    /// <summary>
    /// 要求当前位置出现指定文本。
    /// </summary>
    /// <param name="text">期望文本。</param>
    private void Expect(string text)
    {
        if (!Take(text))
        {
            throw new ApprovalExpressionException($"表达式在第 {offset + 1} 个字符附近缺少 {text}。");
        }
    }

    /// <summary>
    /// 跳过空白。
    /// </summary>
    private void SkipSpace()
    {
        while (offset < source.Length && char.IsWhiteSpace(source[offset]))
        {
            offset++;
        }
    }

    /// <summary>
    /// 限制语法单元数，避免极宽表达式占用过多资源。
    /// </summary>
    private void CountToken()
    {
        if (++tokens > 512)
        {
            throw new ApprovalExpressionException("表达式操作数过多。");
        }
    }
}
