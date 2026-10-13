using System.Diagnostics;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 校验产品源码的提交完整性，防止工作目录依赖被忽略或尚未登记的文件，导致新克隆无法构建。
/// </summary>
internal static class RepositorySourceChecks
{
    /// <summary>
    /// 对照工作目录与 Git 索引，并验证源码日志模块和运行日志的忽略边界。
    /// </summary>
    /// <param name="repositoryRoot">独立仓库根目录。</param>
    /// <param name="check">检查结果回调。</param>
    public static void Run(string repositoryRoot, Action<string, bool> check)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var indexed = ReadGit(repositoryRoot, "ls-files", "-z", "--", "backend/src", "client/src", "frontend/src")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(comparison);
        var missing = new[] { "backend/src", "client/src", "frontend/src" }
            .SelectMany(part => ProductSources(Path.Combine(repositoryRoot, part)))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Where(path => !indexed.Contains(path)).Order(comparison).ToArray();
        check("产品源码全部登记在 Git 索引，不能依赖忽略或未登记文件" + (missing.Length > 0 ? "：" + string.Join("、", missing) : ""), missing.Length == 0);

        string[] probes =
        [
            "backend/src/ApprovalService.Domain/Logs/SourceRegistrationProbe.cs",
            "backend/src/ApprovalService.Application/Logs/SourceRegistrationProbe.cs",
            "logs/runtime-probe.log",
            "backend/logs/runtime-probe.txt",
        ];
        // --no-index 同时检查已登记文件适用的规则，防止强制添加源码暂时掩盖过宽的目录忽略规则。
        var ignored = ReadGit(repositoryRoot, ["check-ignore", "--no-index", "--", .. probes]).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(comparison);
        check("Domain 和 Application 的 Logs 源码目录不会被运行日志规则误忽略", !ignored.Contains(probes[0]) && !ignored.Contains(probes[1]));
        check("仓库与组件运行日志仍被 Git 忽略", ignored.Contains(probes[2]) && ignored.Contains(probes[3]));
    }

    /// <summary>
    /// 枚举三个产品源码目录中的代码与项目文件，排除构建和依赖产物。
    /// </summary>
    /// <param name="directory">产品源码目录。</param>
    /// <returns>应登记在 Git 索引中的源文件路径。</returns>
    private static IEnumerable<string> ProductSources(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        string[] artifacts = ["bin", "obj", "node_modules", "dist"];
        string[] extensions = [".cs", ".csproj", ".ts", ".vue", ".js", ".mjs", ".tsx", ".jsx", ".mts", ".cts"];
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Any(part => artifacts.Contains(part, StringComparer.OrdinalIgnoreCase)))
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 执行只读 Git 查询；非预期错误不能按通过处理，也不读取任何运行日志或凭据。
    /// </summary>
    /// <param name="repositoryRoot">当前隔离仓库的根目录。</param>
    /// <param name="arguments">只读 Git 命令参数。</param>
    /// <returns>Git 命令的标准输出。</returns>
    private static string ReadGit(string repositoryRoot, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repositoryRoot, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Git 源码完整性检查");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && !(arguments[0] == "check-ignore" && process.ExitCode == 1))
        {
            throw new InvalidOperationException($"Git 源码完整性检查失败（{process.ExitCode}）：{error}");
        }

        return output;
    }
}
