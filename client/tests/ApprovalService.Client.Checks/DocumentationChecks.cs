using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 注释规范（AGENTS.md R5）：SDK、演示程序、检查程序的每个类型和成员（包括私有的）都有完整的 XML 注释——三行写法的 summary、
/// 每个参数的 param、有返回值时的 returns、泛型参数的 typeparam；重写和接口实现可以用 inheritdoc。代码里不写任务编号。
/// </summary>
/// <remarks>
/// 做法和后端检查程序的 DocumentationChecks 一样（三个部分互不引用，所以各有一份）：编译器把写了注释的成员都输出到 XML 文档文件里，
/// 这里用反射列出程序集里的类型和成员，逐个到 XML 文档里核对。SDK 按 net8.0 的编译结果核对，演示程序按 net10.0-windows 的编译结果核对；
/// 按框架区分的代码（#if）只出现在方法体里，不影响成员列表。
/// </remarks>
internal static partial class DocumentationChecks
{
    /// <summary>
    /// 反射时列出的成员范围：本类型声明的，公开的和不公开的，实例的和静态的。
    /// </summary>
    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    /// 报错时每个程序集最多列出几处问题。
    /// </summary>
    private const int MaxListed = 30;

    /// <summary>
    /// 是否列出全部问题（只检查注释时用）；否则每个程序集最多列 30 处。
    /// </summary>
    public static bool ListAll
    {
        get;
        set;
    }

    /// <summary>
    /// 运行检查：SDK、检查程序、演示程序的注释，以及源码里的写法和任务编号。
    /// </summary>
    /// <param name="clientRoot">client 目录。</param>
    /// <param name="check">记录检查结果。</param>
    public static void Run(string clientRoot, Action<string, bool> check)
    {
        var constructors = ExplicitDefaultConstructors(SourceFiles(clientRoot, "*.cs"));
        foreach (var assembly in new[] { typeof(ApprovalServiceClient).Assembly, typeof(Demo.Integration.ApprovalConnection).Assembly, typeof(DocumentationChecks).Assembly })
        {
            Report(check, assembly.GetName().Name!, Inspect(assembly, constructors));
        }

        CheckSourceText(clientRoot, check);
    }

    /// <summary>
    /// 检查一个程序集：每个类型和成员都在 XML 文档里有完整的注释。
    /// </summary>
    /// <param name="assembly">程序集，XML 文档文件和它放在一起。</param>
    /// <param name="explicitConstructors">源码里手写了无参构造函数的类型名（没写的无参构造函数是编译器补的，不用注释）。</param>
    /// <returns>每处问题一条说明；没有问题时为空。</returns>
    public static List<string> Inspect(Assembly assembly, IReadOnlySet<string> explicitConstructors)
    {
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        if (!File.Exists(path))
        {
            return [$"没有 XML 文档文件 {Path.GetFileName(path)}（项目要打开 GenerateDocumentationFile）"];
        }

        var members = XDocument.Load(path).Descendants("member")
            .GroupBy(member => (string)member.Attribute("name")!)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var problems = new List<string>();
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>().ToArray();
        }

        // 编译器生成的类型（名字里有尖括号，包括嵌套在它们里面的）不算。
        foreach (var type in types.Where(type => !TypeId(type).Contains('<') && !IsGenerated(type)))
        {
            InspectType(type, members, explicitConstructors, problems);
        }

        return problems;
    }

    /// <summary>
    /// 把一个程序集的检查结果记下来，问题太多时只列前面一部分。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    /// <param name="name">程序集名。</param>
    /// <param name="problems">问题清单。</param>
    private static void Report(Action<string, bool> check, string name, List<string> problems)
    {
        var shown = ListAll ? problems : problems.Take(MaxListed);
        var separator = ListAll ? Environment.NewLine + "    " : "；";
        var listed = problems.Count == 0 ? "" : $"（{problems.Count} 处）：" + (ListAll ? separator : "") + string.Join(separator, shown) + (!ListAll && problems.Count > MaxListed ? "……" : "");
        check($"{name} 的类型和成员都有完整的 XML 注释{listed}", problems.Count == 0);
    }

    /// <summary>
    /// 检查一个类型和它自己声明的成员。
    /// </summary>
    /// <param name="type">类型。</param>
    /// <param name="members">XML 文档里的全部成员，按标识索引。</param>
    /// <param name="explicitConstructors">源码里手写了无参构造函数的类型名。</param>
    /// <param name="problems">问题清单，往里追加。</param>
    private static void InspectType(Type type, Dictionary<string, XElement> members, IReadOnlySet<string> explicitConstructors, List<string> problems)
    {
        var typeId = TypeId(type);
        var typeParameters = type.IsGenericTypeDefinition ? type.GetGenericArguments().Length - (type.DeclaringType?.GetGenericArguments().Length ?? 0) : 0;
        var invoke = typeof(MulticastDelegate).IsAssignableFrom(type) ? type.GetMethod("Invoke") : null;
        Verify(problems, members, "T:" + typeId, Display(type), invoke?.GetParameters() ?? [], invoke?.ReturnType, typeParameters);
        if (invoke is not null)
        {
            return;
        }

        foreach (var member in type.GetMembers(Declared).Where(member => !IsGenerated(member)))
        {
            switch (member)
            {
                // 静态构造函数（编译器为静态字段初始化补的）和编译器补的无参构造函数不算。
                case ConstructorInfo constructor when constructor.IsStatic || (constructor.GetParameters().Length == 0 && !explicitConstructors.Contains(BaseName(type))):
                    break;
                case ConstructorInfo constructor:
                    VerifyMethod(problems, members, typeId, type, constructor, "#ctor", null);
                    break;
                // [GeneratedRegex] 的实现由源生成器提供，编译器用的是生成器写的注释；源码里的注释由 CheckSourceText 按文本核对。
                case MethodInfo method when method.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name == "GeneratedRegexAttribute"):
                    break;
                case MethodInfo method when !IsAccessor(method):
                    VerifyMethod(problems, members, typeId, type, method, MemberName(method.Name), method.ReturnType);
                    break;
                case PropertyInfo property:
                    var indexer = property.GetIndexParameters();
                    var propertyId = $"P:{typeId}.{MemberName(property.Name)}" + (indexer.Length > 0 ? "(" + string.Join(",", indexer.Select(parameter => ParameterTypeId(parameter.ParameterType))) + ")" : "");
                    Verify(problems, members, propertyId, $"{Display(type)}.{property.Name}", indexer, null, 0);
                    break;
                case FieldInfo field when !field.IsSpecialName:
                    Verify(problems, members, $"F:{typeId}.{field.Name}", $"{Display(type)}.{field.Name}", [], null, 0);
                    break;
                case EventInfo @event:
                    Verify(problems, members, $"E:{typeId}.{MemberName(@event.Name)}", $"{Display(type)}.{@event.Name}", [], null, 0);
                    break;
            }
        }
    }

    /// <summary>
    /// 检查一个方法或构造函数。
    /// </summary>
    /// <param name="problems">问题清单。</param>
    /// <param name="members">XML 文档里的全部成员。</param>
    /// <param name="typeId">所在类型的标识。</param>
    /// <param name="type">所在类型。</param>
    /// <param name="method">方法或构造函数。</param>
    /// <param name="name">文档标识里的成员名（构造函数是 #ctor）。</param>
    /// <param name="returnType">返回类型；构造函数为 null。</param>
    private static void VerifyMethod(List<string> problems, Dictionary<string, XElement> members, string typeId, Type type, MethodBase method, string name, Type? returnType)
    {
        var parameters = method.GetParameters();
        var arity = method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0;
        var id = new StringBuilder($"M:{typeId}.{name}");
        if (arity > 0)
        {
            id.Append("``").Append(arity);
        }

        if (parameters.Length > 0)
        {
            id.Append('(').Append(string.Join(",", parameters.Select(parameter => ParameterTypeId(parameter.ParameterType)))).Append(')');
        }

        // 转换运算符的标识要带返回类型。
        if (method.Name is "op_Implicit" or "op_Explicit" && returnType is not null)
        {
            id.Append('~').Append(ParameterTypeId(returnType));
        }

        Verify(problems, members, id.ToString(), $"{Display(type)}.{method.Name}({string.Join(", ", parameters.Select(parameter => parameter.Name))})", parameters, returnType, arity);
    }

    /// <summary>
    /// 核对一个成员的注释：有 summary 或 inheritdoc；每个参数有 param；有返回值时有 returns；泛型参数有 typeparam；说明都不为空。
    /// </summary>
    /// <param name="problems">问题清单。</param>
    /// <param name="members">XML 文档里的全部成员。</param>
    /// <param name="id">成员在 XML 文档里的标识。</param>
    /// <param name="display">报错时显示的名字。</param>
    /// <param name="parameters">参数。</param>
    /// <param name="returnType">返回类型；没有返回值或不适用时为 null。</param>
    /// <param name="typeParameters">泛型参数个数。</param>
    private static void Verify(List<string> problems, Dictionary<string, XElement> members, string id, string display, ParameterInfo[] parameters, Type? returnType, int typeParameters)
    {
        if (!members.TryGetValue(id, out var doc))
        {
            problems.Add($"{display} 没有注释");
            return;
        }

        // 重写、接口实现沿用基类或接口的注释。
        if (doc.Element("inheritdoc") is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(doc.Element("summary")?.Value))
        {
            problems.Add($"{display} 没有 summary");
        }

        var documented = doc.Elements("param")
            .Where(param => !string.IsNullOrWhiteSpace(param.Value))
            .Select(param => (string?)param.Attribute("name"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var parameter in parameters.Where(parameter => parameter.Name is not null && !documented.Contains(parameter.Name)))
        {
            problems.Add($"{display} 缺参数 {parameter.Name} 的说明");
        }

        if (returnType is not null && returnType != typeof(void) && string.IsNullOrWhiteSpace(doc.Element("returns")?.Value))
        {
            problems.Add($"{display} 缺返回值说明");
        }

        if (doc.Elements("typeparam").Count(param => !string.IsNullOrWhiteSpace(param.Value)) < typeParameters)
        {
            problems.Add($"{display} 缺泛型参数说明");
        }
    }

    /// <summary>
    /// 按文本检查源码：summary 写成三行（不写在一行里）；正则写了注释；代码、项目文件和脚本里没有任务编号。
    /// </summary>
    /// <param name="clientRoot">client 目录。</param>
    /// <param name="check">记录检查结果。</param>
    private static void CheckSourceText(string clientRoot, Action<string, bool> check)
    {
        string Relative(string path) => Path.GetRelativePath(clientRoot, path).Replace('\\', '/');
        var sources = SourceFiles(clientRoot, "*.cs").Where(path => !IsGeneratedFile(path)).ToArray();
        var oneLine = sources
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(item => OneLineSummary().IsMatch(item.line))
            .Select(item => $"{Relative(item.path)}:{item.index + 1}")
            .ToArray();
        check("summary 都写成三行（/// <summary>、说明、/// </summary>）" + (oneLine.Length > 0 ? $"（{oneLine.Length} 处）：" + string.Join("、", ListAll ? oneLine : oneLine.Take(MaxListed)) : ""), oneLine.Length == 0);

        // GeneratedRegex 的分部方法由源生成器补上它自己的注释，反射核对不到，这里按文本要求声明上方有 ///。
        var bareRegex = sources
            .SelectMany(path =>
            {
                var lines = File.ReadAllLines(path);
                return lines.Select((line, index) => (line, index))
                    .Where(item => item.line.TrimStart().StartsWith("[GeneratedRegex", StringComparison.Ordinal) && !PrecededByDoc(lines, item.index))
                    .Select(item => $"{Relative(path)}:{item.index + 1}");
            })
            .ToArray();
        check("正则（GeneratedRegex）都写了注释" + (bareRegex.Length > 0 ? "：" + string.Join("、", bareRegex) : ""), bareRegex.Length == 0);

        // 任务编号只出现在任务文件、提交信息和文档里，代码里写清楚“为什么”就够了。
        string[] patterns = ["*.cs", "*.csproj", "*.props", "*.ps1", "*.json", "*.yml"];
        var withTaskId = patterns.SelectMany(pattern => SourceFiles(clientRoot, pattern))
            .Distinct()
            .Where(path => !IsGeneratedFile(path) && TaskId().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();
        check("代码、项目文件和脚本里没有任务编号" + (withTaskId.Length > 0 ? "：" + string.Join("、", withTaskId) : ""), withTaskId.Length == 0);
    }

    /// <summary>
    /// 编译器生成的类型或成员（不用写注释）：名字里有尖括号（闭包、lambda、本地函数、备份字段）或标了 CompilerGenerated。
    /// </summary>
    /// <param name="member">类型或成员。</param>
    /// <returns>是编译器生成的时为 true。</returns>
    private static bool IsGenerated(MemberInfo member) =>
        member.Name.Contains('<') || member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    /// <summary>
    /// 属性、事件的访问器（跟着属性、事件一起核对）。
    /// </summary>
    /// <param name="method">方法。</param>
    /// <returns>是访问器时为 true。</returns>
    private static bool IsAccessor(MethodInfo method) =>
        method.IsSpecialName && (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal)
            || method.Name.StartsWith("add_", StringComparison.Ordinal) || method.Name.StartsWith("remove_", StringComparison.Ordinal));

    /// <summary>
    /// 工具生成的源码文件（开头几行有 auto-generated 标记）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>是生成的文件时为 true。</returns>
    private static bool IsGeneratedFile(string path) =>
        path.EndsWith(".cs", StringComparison.Ordinal) && File.ReadLines(path).Take(3).Any(line => line.Contains("<auto-generated", StringComparison.Ordinal));

    /// <summary>
    /// 类型在 XML 文档里的标识：命名空间加类型名，嵌套类型用点连接，泛型带 `n。
    /// </summary>
    /// <param name="type">类型（不是构造出来的泛型）。</param>
    /// <returns>如 ApprovalService.Client.ApprovalServiceClient。</returns>
    private static string TypeId(Type type) => (type.FullName ?? $"{type.Namespace}.{type.Name}").Replace('+', '.');

    /// <summary>
    /// 参数类型在 XML 文档标识里的写法：构造出来的泛型写成 {参数}，泛型参数写成 `n 或 ``n，引用参数加 @，数组加 []。
    /// </summary>
    /// <param name="type">参数类型。</param>
    /// <returns>标识里的类型写法。</returns>
    private static string ParameterTypeId(Type type)
    {
        if (type.IsByRef)
        {
            return ParameterTypeId(type.GetElementType()!) + "@";
        }

        if (type.IsPointer)
        {
            return ParameterTypeId(type.GetElementType()!) + "*";
        }

        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            return ParameterTypeId(type.GetElementType()!) + (rank == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]");
        }

        if (type.IsGenericParameter)
        {
            return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;
        }

        if (!type.IsGenericType)
        {
            return TypeId(type);
        }

        // 泛型（在泛型类型里面引用它自己时，反射给的是泛型定义）：去掉 `n，参数写在大括号里。嵌套在泛型里的类型这里不处理，调用端没有用到。
        var definition = TypeId(type.GetGenericTypeDefinition());
        var tick = definition.LastIndexOf('`');
        var name = tick < 0 ? definition : definition[..tick];
        return name + "{" + string.Join(",", type.GetGenericArguments().Select(ParameterTypeId)) + "}";
    }

    /// <summary>
    /// 源码里手写了无参构造函数的类型名（访问修饰符后面直接跟类型名和空括号的，只能是构造函数）。
    /// </summary>
    /// <param name="sources">源码文件。</param>
    /// <returns>类型名集合。</returns>
    private static HashSet<string> ExplicitDefaultConstructors(IEnumerable<string> sources) =>
        sources.SelectMany(File.ReadLines)
            .Select(line => DefaultConstructor().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 类型名去掉泛型的 `n。
    /// </summary>
    /// <param name="type">类型。</param>
    /// <returns>如 Payload。</returns>
    private static string BaseName(Type type) => type.Name.Split('`')[0];

    /// <summary>
    /// 成员名在 XML 文档标识里的写法：显式接口实现的名字里的点换成 #。
    /// </summary>
    /// <param name="name">反射得到的成员名。</param>
    /// <returns>标识里的成员名。</returns>
    private static string MemberName(string name) => name.Replace('.', '#');

    /// <summary>
    /// 报错时显示的类型名：去掉命名空间前缀 ApprovalService.。
    /// </summary>
    /// <param name="type">类型。</param>
    /// <returns>如 Client.ApprovalServiceClient。</returns>
    private static string Display(Type type) => TypeId(type).Replace("ApprovalService.", "", StringComparison.Ordinal);

    /// <summary>
    /// 目录下的源码文件，跳过 bin、obj。
    /// </summary>
    /// <param name="directory">目录；不存在时没有文件。</param>
    /// <param name="pattern">文件名模式。</param>
    /// <returns>文件的完整路径。</returns>
    private static IEnumerable<string> SourceFiles(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var separator = Path.DirectorySeparatorChar;
        string[] skipped = ["bin", "obj", "artifacts"];
        return Directory.GetFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(path => !skipped.Any(name => path.Contains($"{separator}{name}{separator}", StringComparison.Ordinal)));
    }

    /// <summary>
    /// 第 index 行的声明上方（跳过其他特性行）是不是 XML 注释。
    /// </summary>
    /// <param name="lines">源文件的全部行。</param>
    /// <param name="index">声明（或它的第一个特性）所在的行号，从 0 开始。</param>
    /// <returns>上方紧挨着 /// 时为 true。</returns>
    private static bool PrecededByDoc(string[] lines, int index)
    {
        var above = index - 1;
        while (above >= 0 && lines[above].TrimStart().StartsWith('['))
        {
            above--;
        }

        return above >= 0 && lines[above].TrimStart().StartsWith("///", StringComparison.Ordinal);
    }

    /// <summary>
    /// 手写的无参构造函数：访问修饰符后面直接是类型名和空括号。
    /// </summary>
    /// <returns>编译好的正则。</returns>
    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal)\s+)+(?<name>[A-Z]\w*)\s*\(\s*\)")]
    private static partial Regex DefaultConstructor();

    /// <summary>
    /// 写在一行里的 summary。
    /// </summary>
    /// <returns>编译好的正则。</returns>
    [GeneratedRegex(@"^\s*///\s*<summary>.*</summary>")]
    private static partial Regex OneLineSummary();

    /// <summary>
    /// 任务编号：T、连字符和三位数字。
    /// </summary>
    /// <returns>编译好的正则。</returns>
    [GeneratedRegex(@"\bT-\d{3}\b")]
    private static partial Regex TaskId();
}
