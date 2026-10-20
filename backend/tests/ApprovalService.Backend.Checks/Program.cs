namespace ApprovalService.Backend.Checks;

/// <summary>
/// 后端检查程序入口。按“契约 → 服务端 → 架构与仓库规范 → 注释”的顺序执行，任何一项失败立即抛异常退出。
/// </summary>
/// <remarks>
/// 带参数 <c>docs</c> 时只检查注释（补注释时用，几秒钟就出结果）：<c>dotnet run --project backend/tests/ApprovalService.Backend.Checks -- docs</c>。
/// 新的检查按主题放在 XxxChecks.cs 里，在这里加一行调用（AGENTS.md §5）。
/// </remarks>
internal static class Program
{
    /// <summary>
    /// 已通过的检查项数。
    /// </summary>
    private static int passed;

    /// <summary>
    /// 运行全部检查。
    /// </summary>
    /// <param name="args">命令行参数；含 docs 时只检查注释。</param>
    /// <returns>全部通过时为 0；有失败时抛异常，进程以非零退出。</returns>
    private static async Task<int> Main(string[] args)
    {
        var backendRoot = Fixtures.BackendRoot();
        var repositoryRoot = Directory.GetParent(backendRoot)!.FullName;
        if (args.Contains("docs"))
        {
            // 只检查注释：列出全部问题，不在第一项失败时停下。
            var failed = 0;
            DocumentationChecks.ListAll = true;
            DocumentationChecks.Run(backendRoot, repositoryRoot, (name, value) =>
            {
                Console.WriteLine((value ? "PASS: " : "FAIL: ") + name);
                failed += value ? 0 : 1;
            });
            Console.WriteLine(failed == 0 ? "注释检查全部通过" : $"注释检查有 {failed} 项没通过");
            return failed == 0 ? 0 : 1;
        }

        ContractChecks.Run(repositoryRoot, Check);
        FlowDefinitionChecks.Run(backendRoot, Check);
        EngineScenarioChecks.Run(Check);
        EngineFuzzChecks.Run(Check);
        AssigneeChecks.Run(Check);
        ApprovalPolicyChecks.Run(Check);
        OperationChecks.Run(Check);
        OperationChecksBoundaries.Run(Check);
        await DatabaseProviderChecks.RunAsync(Check);
        await PersistenceChecks.RunAsync(Check);
        await EventAnchorChecks.RunAsync(Check);
        await EventChainConsistencyChecks.RunAsync(Check);
        await JobChecks.RunAsync(Check);
        ApprovalDataChecks.Run(Check);
        await OpenApiChecksClientApps.RunAsync(Check);
        await OpenApiChecksFlows.RunAsync(Check);
        await OpenApiChecksCallbacks.RunAsync(Check);
        await OpenApiChecksActiveKeys.RunAsync(Check);
        await OpenApiChecksQueries.RunAsync(Check);
        await OpenApiChecksQueryHttp.RunAsync(backendRoot, Check);
        await OpenApiChecksTasks.RunAsync(backendRoot, Check);
        OpenApiChecksData.Run(Check);
        await IdentityAppServiceChecks.RunAsync(Check);
        await OrganizationChecks.RunAsync(Check);
        await IdentityEndpointChecks.RunAsync(backendRoot, Check);

        await ServerChecks.RunAsync(backendRoot, Check);
        ArchitectureChecks.Run(backendRoot, repositoryRoot, Check);
        RepositorySourceChecks.Run(repositoryRoot, Check);
        DocumentationChecks.Run(backendRoot, repositoryRoot, Check);
        Console.WriteLine($"后端检查全部通过：{passed} 项");
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
}
