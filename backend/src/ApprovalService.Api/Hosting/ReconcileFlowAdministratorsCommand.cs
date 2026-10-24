using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Administration;

namespace ApprovalService.Api.Hosting;

/// <summary>
/// 服务器本机预览并提交一次存量流程管理员内部 ID 修正。
/// </summary>
internal static class ReconcileFlowAdministratorsCommand
{
    /// <summary>
    /// 先显示唯一修正与歧义，再由操作人在控制台确认提交。
    /// </summary>
    /// <param name="services">已配置的服务容器。</param>
    /// <returns>成功为零，取消或冲突为一。</returns>
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("请在服务器本机交互式控制台预览并确认流程管理员身份修正。");
            return 1;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var reconciler = scope.ServiceProvider.GetRequiredService<FlowAdministratorIdentityReconciler>();
            await reconciler.PrepareDatabaseAsync();
            var preview = await reconciler.PreviewAsync();
            Console.WriteLine($"唯一匹配、待修正：{preview.PlannedUpdates.Count} 条；需人工处理：{preview.Unresolved.Count} 条。");
            foreach (var update in preview.PlannedUpdates)
            {
                Console.WriteLine($"  修正 {update.FlowCode}: {update.LegacyValue} → {update.ResolvedPersonId}");
            }

            foreach (var issue in preview.Unresolved)
            {
                Console.WriteLine($"  待人工处理 {issue.FlowCode}: {issue.LegacyValue}，匹配 {issue.MatchCount} 人");
            }

            Console.Write("输入 APPLY 提交上述修正及待处理记录：");
            if (Console.ReadLine() != "APPLY")
            {
                Console.Error.WriteLine("已取消，数据库未改变。");
                return 1;
            }

            await reconciler.ReconcileAsync(preview.Fingerprint);
            Console.WriteLine("流程管理员身份对账已提交；需人工处理的记录保留在数据库中。");
            return 0;
        }
        catch (UserFriendlyException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
