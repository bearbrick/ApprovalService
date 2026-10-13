using System.Collections;
using System.Globalization;
using System.Text;

namespace ApprovalService.Client.Internal;

/// <summary>
/// SDK 自带的小 JSON 读写，只覆盖开放接口用到的部分，不依赖 System.Text.Json（这样 SDK 能支持 .NET Framework 4.6.1，也不会和调用方已有的 DLL 版本冲突）。
/// </summary>
/// <remarks>
/// 写：null、string、bool、整数、decimal、double、float、字典（对象，按添加顺序写属性）、其他可枚举（数组）；其余类型按不变区域性转成字符串。
/// 读：对象读成 <see cref="Dictionary{TKey,TValue}"/>，数组读成 <see cref="List{T}"/>，数字读成 decimal（超出范围时 double），
/// 字符串、true、false、null 读成对应的值。格式不对时抛 <see cref="FormatException"/>。
/// </remarks>
internal static class JsonText
{
    /// <summary>
    /// 对象、数组最多嵌套几层（防止恶意的深层嵌套把栈用完）。
    /// </summary>
    private const int MaxDepth = 64;

    /// <summary>
    /// 序列化成 UTF-8 的 JSON。
    /// </summary>
    /// <param name="value">值，一般是字典。</param>
    /// <returns>UTF-8 字节（不带 BOM）。</returns>
    /// <exception cref="ArgumentException">数字是 NaN 或无穷大。</exception>
    public static byte[] Serialize(object? value)
    {
        var text = new StringBuilder();
        Write(text, value, 0);
        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    /// <summary>
    /// 解析 JSON 文本。
    /// </summary>
    /// <param name="text">JSON 文本，开头可以带 BOM。</param>
    /// <returns>解析出的值。</returns>
    /// <exception cref="FormatException">不是合法的 JSON，或嵌套太深。</exception>
    public static object? Parse(string text)
    {
        var reader = new Reader(text);
        return reader.ReadDocument();
    }

    /// <summary>
    /// 写一个值。
    /// </summary>
    /// <param name="text">输出。</param>
    /// <param name="value">值。</param>
    /// <param name="depth">当前嵌套层数。</param>
    private static void Write(StringBuilder text, object? value, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new ArgumentException("请求数据嵌套太深。");
        }

        switch (value)
        {
            case null:
                text.Append("null");
                return;
            case string s:
                WriteString(text, s);
                return;
            case bool b:
                text.Append(b ? "true" : "false");
                return;
            case byte _:
            case sbyte _:
            case short _:
            case ushort _:
            case int _:
            case uint _:
            case long _:
            case ulong _:
            case decimal _:
                text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            case double d:
                WriteDouble(text, d);
                return;
            case float f:
                WriteDouble(text, f);
                return;
            case IDictionary dictionary:
                text.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    text.Append(first ? "" : ",");
                    first = false;
                    WriteString(text, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "");
                    text.Append(':');
                    Write(text, entry.Value, depth + 1);
                }

                text.Append('}');
                return;
            case IEnumerable list:
                text.Append('[');
                var firstItem = true;
                foreach (var item in list)
                {
                    text.Append(firstItem ? "" : ",");
                    firstItem = false;
                    Write(text, item, depth + 1);
                }

                text.Append(']');
                return;
            default:
                WriteString(text, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                return;
        }
    }

    /// <summary>
    /// 写浮点数：按不变区域性、能还原原值的最短写法。
    /// </summary>
    /// <param name="text">输出。</param>
    /// <param name="value">数值。</param>
    /// <exception cref="ArgumentException">NaN 或无穷大（JSON 里没有这种数）。</exception>
    private static void WriteDouble(StringBuilder text, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("请求数据里的数字不能是 NaN 或无穷大。");
        }

        text.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 写字符串：转义引号、反斜杠和控制字符，中文等非 ASCII 字符原样写（UTF-8）。
    /// </summary>
    /// <param name="text">输出。</param>
    /// <param name="value">字符串。</param>
    private static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                default:
                    if (c < ' ')
                    {
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append('"');
    }

    /// <summary>
    /// 递归下降的 JSON 解析器。
    /// </summary>
    private sealed class Reader
    {
        /// <summary>
        /// 要解析的文本。
        /// </summary>
        private readonly string text;

        /// <summary>
        /// 当前读到的位置。
        /// </summary>
        private int position;

        /// <summary>
        /// 准备解析；跳过开头的 BOM。
        /// </summary>
        /// <param name="text">JSON 文本。</param>
        public Reader(string text)
        {
            this.text = text ?? "";
            position = this.text.Length > 0 && this.text[0] == '﻿' ? 1 : 0;
        }

        /// <summary>
        /// 读整个文档：一个值，后面只能有空白。
        /// </summary>
        /// <returns>值。</returns>
        public object? ReadDocument()
        {
            SkipWhitespace();
            var value = ReadValue(0);
            SkipWhitespace();
            if (position != text.Length)
            {
                throw Error("值后面还有多余的内容");
            }

            return value;
        }

        /// <summary>
        /// 读一个值。
        /// </summary>
        /// <param name="depth">当前嵌套层数。</param>
        /// <returns>值。</returns>
        private object? ReadValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Error("嵌套太深");
            }

            if (position >= text.Length)
            {
                throw Error("内容不完整");
            }

            switch (text[position])
            {
                case '{':
                    return ReadObject(depth);
                case '[':
                    return ReadArray(depth);
                case '"':
                    return ReadString();
                case 't':
                    Expect("true");
                    return true;
                case 'f':
                    Expect("false");
                    return false;
                case 'n':
                    Expect("null");
                    return null;
                default:
                    return ReadNumber();
            }
        }

        /// <summary>
        /// 读对象，属性名区分大小写，同名属性后面的覆盖前面的。
        /// </summary>
        /// <param name="depth">当前嵌套层数。</param>
        /// <returns>属性名 → 值。</returns>
        private Dictionary<string, object?> ReadObject(int depth)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            position++;
            SkipWhitespace();
            if (Peek() == '}')
            {
                position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                {
                    throw Error("属性名要用双引号");
                }

                var name = ReadString();
                SkipWhitespace();
                if (Peek() != ':')
                {
                    throw Error("属性名后面缺少冒号");
                }

                position++;
                SkipWhitespace();
                result[name] = ReadValue(depth + 1);
                SkipWhitespace();
                var next = Peek();
                position++;
                if (next == '}')
                {
                    return result;
                }

                if (next != ',')
                {
                    throw Error("对象里缺少逗号或右花括号");
                }
            }
        }

        /// <summary>
        /// 读数组。
        /// </summary>
        /// <param name="depth">当前嵌套层数。</param>
        /// <returns>元素。</returns>
        private List<object?> ReadArray(int depth)
        {
            var result = new List<object?>();
            position++;
            SkipWhitespace();
            if (Peek() == ']')
            {
                position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                result.Add(ReadValue(depth + 1));
                SkipWhitespace();
                var next = Peek();
                position++;
                if (next == ']')
                {
                    return result;
                }

                if (next != ',')
                {
                    throw Error("数组里缺少逗号或右方括号");
                }
            }
        }

        /// <summary>
        /// 读字符串（当前位置是左引号），处理转义和 \uXXXX。
        /// </summary>
        /// <returns>字符串。</returns>
        private string ReadString()
        {
            var result = new StringBuilder();
            position++;
            while (position < text.Length)
            {
                var c = text[position++];
                if (c == '"')
                {
                    return result.ToString();
                }

                if (c != '\\')
                {
                    if (c < ' ')
                    {
                        throw Error("字符串里有没转义的控制字符");
                    }

                    result.Append(c);
                    continue;
                }

                if (position >= text.Length)
                {
                    break;
                }

                var escape = text[position++];
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        result.Append(escape);
                        break;
                    case 'b':
                        result.Append('\b');
                        break;
                    case 'f':
                        result.Append('\f');
                        break;
                    case 'n':
                        result.Append('\n');
                        break;
                    case 'r':
                        result.Append('\r');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    case 'u':
                        if (position + 4 > text.Length || !int.TryParse(text.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                        {
                            throw Error("\\u 后面要有 4 位十六进制数");
                        }

                        result.Append((char)code);
                        position += 4;
                        break;
                    default:
                        throw Error("不认识的转义字符 \\" + escape);
                }
            }

            throw Error("字符串没有结束的引号");
        }

        /// <summary>
        /// 读数字：能放进 decimal 的读成 decimal（精确），否则读成 double。
        /// </summary>
        /// <returns>decimal 或 double。</returns>
        private object ReadNumber()
        {
            var start = position;
            while (position < text.Length && "+-0123456789.eE".IndexOf(text[position]) >= 0)
            {
                position++;
            }

            var number = text.Substring(start, position - start);
            if (number.Length == 0)
            {
                throw Error("这里应该是一个值");
            }

            if (decimal.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact))
            {
                return exact;
            }

            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var approximate))
            {
                return approximate;
            }

            throw Error("数字格式不对：" + number);
        }

        /// <summary>
        /// 读一个固定的字面量（true、false、null）。
        /// </summary>
        /// <param name="literal">字面量。</param>
        private void Expect(string literal)
        {
            if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
            {
                throw Error("这里应该是 " + literal);
            }

            position += literal.Length;
        }

        /// <summary>
        /// 跳过空白。
        /// </summary>
        private void SkipWhitespace()
        {
            while (position < text.Length && (text[position] == ' ' || text[position] == '\t' || text[position] == '\n' || text[position] == '\r'))
            {
                position++;
            }
        }

        /// <summary>
        /// 当前位置的字符；到结尾时是 '\0'。
        /// </summary>
        /// <returns>字符。</returns>
        private char Peek() => position < text.Length ? text[position] : '\0';

        /// <summary>
        /// 解析错误，说明里带位置。
        /// </summary>
        /// <param name="message">说明。</param>
        /// <returns>异常（这里只构造，不抛）。</returns>
        private FormatException Error(string message) => new FormatException($"JSON 第 {position + 1} 个字符附近：{message}");
    }
}
