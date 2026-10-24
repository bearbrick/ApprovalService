namespace ApprovalService.Client.Checks;

/// <summary>
/// 调用端检查程序入口。任何一项失败立即抛异常退出。
/// </summary>
/// <remarks>
/// 带参数 <c>docs</c> 时只检查注释：<c>dotnet run --project client/tests/ApprovalService.Client.Checks -- docs</c>。
/// 设置环境变量 APPROVAL_SERVICE_URL 时加跑对真实服务的端到端检查（根目录 verify.ps1 会自动启动服务并设置）。
/// </remarks>
internal static class Program
{
    /// <summary>
    /// 已通过的检查项数。
    /// </summary>
    private static int passed;

    /// <summary>
    /// 只检查注释时没通过的项数。
    /// </summary>
    private static int failed;

    /// <summary>
    /// 运行全部检查。
    /// </summary>
    /// <param name="args">命令行参数；含 docs 时只检查注释。</param>
    /// <returns>全部通过时为 0。</returns>
    private static async Task<int> Main(string[] args)
    {
        var clientRoot = ClientRoot();
        if (args.Contains("docs"))
        {
            DocumentationChecks.ListAll = true;
            DocumentationChecks.Run(clientRoot, Report);
            return failed == 0 ? 0 : 1;
        }

        await ClientChecks.RunAsync(Check);
        await OpenApiChecks.RunAsync(Check);
        await AdvancedApiChecks.RunAsync(Check);
        DemoIntegrationChecks.Run(Check);
        ArchitectureChecks.Run(clientRoot, Check);
        DocumentationChecks.Run(clientRoot, Check);
        await LiveServerChecks.RunAsync(Check);
        Console.WriteLine($"调用端检查全部通过：{passed} 项");
        return 0;
    }

    /// <summary>
    /// 记录一项检查：通过时打印 PASS，失败时抛异常结束。
    /// </summary>
    /// <param name="name">检查的中文描述。</param>
    /// <param name="value">是否通过。</param>
    /// <exception cref="Exception">没通过。</exception>
    private static void Check(string name, bool value)
    {
        if (!value)
        {
            throw new Exception("FAIL: " + name);
        }

        Console.WriteLine("PASS: " + name);
        passed++;
    }

    /// <summary>
    /// 只检查注释时用：失败也继续，最后汇总。
    /// </summary>
    /// <param name="name">检查的中文描述。</param>
    /// <param name="value">是否通过。</param>
    private static void Report(string name, bool value)
    {
        Console.WriteLine((value ? "PASS: " : "FAIL: ") + name);
        if (!value)
        {
            failed++;
        }
    }

    /// <summary>
    /// 从输出目录逐级向上找到调用端根目录（有 ApprovalService.Client.slnx 的目录）。
    /// </summary>
    /// <returns>client 目录的完整路径。</returns>
    /// <exception cref="DirectoryNotFoundException">不在仓库里运行。</exception>
    private static string ClientRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ApprovalService.Client.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("未找到 ApprovalService.Client.slnx，请在仓库内运行检查程序。");
    }
}
