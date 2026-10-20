using ApprovalService.Application.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApprovalService.Infrastructure.Files;

/// <summary>
/// 周期清理超过保留期且未关联审批单的上传文件。
/// </summary>
/// <param name="scopeFactory">每轮创建独立数据库作用域。</param>
/// <param name="logger">清理失败日志。</param>
public sealed class FileCleanupHostedService(IServiceScopeFactory scopeFactory,
    ILogger<FileCleanupHostedService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IFileAppService>();
                int removed;
                do
                {
                    // 这一轮持续处理满批次，避免长期积压超过保留期的未使用文件。
                    removed = await service.CleanupUnusedAsync(stoppingToken);
                }
                while (removed == FilePolicyOptions.CleanupBatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // 清理失败保留租约与元数据，下轮或其他实例可继续重试。
                logger.LogError(exception, "附件孤儿清理失败。");
            }
        }
    }
}
