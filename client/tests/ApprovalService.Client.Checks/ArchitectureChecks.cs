using System.Text.RegularExpressions;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 调用端的约定（AGENTS.md R4、R11）：SDK 兼容 .NET Framework 4.6.1、不依赖任何包；对接类库不依赖界面框架、能在 .NET Framework 4.8 上编译。
/// </summary>
internal static partial class ArchitectureChecks
{
    /// <summary>
    /// 运行检查。
    /// </summary>
    /// <param name="clientRoot">client 目录。</param>
    /// <param name="check">记录检查结果。</param>
    public static void Run(string clientRoot, Action<string, bool> check)
    {
        string Relative(string path) => Path.GetRelativePath(clientRoot, path).Replace('\\', '/');

        var sdkProject = File.ReadAllText(Path.Combine(clientRoot, "src", "ApprovalService.Client", "ApprovalService.Client.csproj"));
        check("SDK 不引用任何项目", !sdkProject.Contains("<ProjectReference"));
        check("SDK 的目标框架是 net461、netstandard2.0、net8.0", sdkProject.Contains("<TargetFrameworks>net461;netstandard2.0;net8.0</TargetFrameworks>"));
        check("SDK 不依赖任何 NuGet 包（JSON 读写是自带的）", !sdkProject.Contains("<PackageReference"));

        var integrationProject = File.ReadAllText(Path.Combine(clientRoot, "samples", "ApprovalService.Demo.Integration", "ApprovalService.Demo.Integration.csproj"));
        check("对接类库编译到 .NET Framework 4.8 和 .NET 10，只引用 SDK", integrationProject.Contains("<TargetFrameworks>net48;net10.0</TargetFrameworks>")
            && Regex.Count(integrationProject, "<ProjectReference") == 1 && integrationProject.Contains(@"ApprovalService.Client\ApprovalService.Client.csproj"));
        check("对接类库不依赖界面框架（WinUI、WinForms、WPF）", !UiFramework().IsMatch(integrationProject)
            && SourceFiles(Path.Combine(clientRoot, "samples", "ApprovalService.Demo.Integration")).All(path => !UiFramework().IsMatch(File.ReadAllText(path))));

        var props = File.ReadAllText(Path.Combine(clientRoot, "src", "Directory.Build.props"));
        check("SDK 强制公开成员写 XML 注释", props.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>") && props.Contains("CS1591"));

        var sources = SourceFiles(Path.Combine(clientRoot, "src")).ToArray();
        var large = sources.Concat(SourceFiles(Path.Combine(clientRoot, "samples"))).Where(path => File.ReadLines(path).Count() >= 500).Select(Relative).ToArray();
        check("SDK 和示例的源码文件都少于 500 行" + (large.Length > 0 ? "：" + string.Join(", ", large) : ""), large.Length == 0);
        var modern = sources.Where(path => NewRuntimeSyntax().IsMatch(File.ReadAllText(path))).Select(Relative).ToArray();
        check("SDK 不用 net461 不支持的 record、init、required" + (modern.Length > 0 ? "：" + string.Join(", ", modern) : ""), modern.Length == 0);
        var heavy = sources.Where(path => ForbiddenDependency().IsMatch(File.ReadAllText(path))).Select(Relative).ToArray();
        check("SDK 不依赖 Newtonsoft.Json、System.Text.Json 和日志框架" + (heavy.Length > 0 ? "：" + string.Join(", ", heavy) : ""), heavy.Length == 0 && !ForbiddenDependency().IsMatch(sdkProject));
    }

    /// <summary>
    /// 列出目录里的 C# 源文件，跳过 bin、obj。
    /// </summary>
    /// <param name="directory">目录；不存在时返回空。</param>
    /// <returns>文件的完整路径。</returns>
    private static IEnumerable<string> SourceFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var separator = Path.DirectorySeparatorChar;
        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}") && !path.Contains($"{separator}obj{separator}"));
    }

    /// <summary>
    /// net461 不支持的语法（需要新运行时类型）。
    /// </summary>
    /// <returns>编译好的正则。</returns>
    [GeneratedRegex(@"\brecord\b|\binit\s*;|\brequired\s+[A-Za-z<]")]
    private static partial Regex NewRuntimeSyntax();

    /// <summary>
    /// SDK 不能依赖的库：第三方 JSON、System.Text.Json（老框架要额外装包；只看 using，注释里提到不算）、日志框架。
    /// </summary>
    /// <returns>编译好的正则。</returns>
    [GeneratedRegex(@"Newtonsoft|using System\.Text\.Json|Microsoft\.Extensions\.Logging|Serilog|NLog|log4net")]
    private static partial Regex ForbiddenDependency();

    /// <summary>
    /// 界面框架的痕迹。
    /// </summary>
    /// <returns>编译好的正则。</returns>
    [GeneratedRegex(@"UseWinUI|UseWindowsForms|UseWPF|Microsoft\.UI\.Xaml|System\.Windows\.Forms|Microsoft\.WindowsAppSDK")]
    private static partial Regex UiFramework();
}
