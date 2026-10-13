using ApprovalService.Client;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 示例业务系统和审批流服务的连接：按配置建好 SDK 客户端，整个程序共用一个。
/// </summary>
/// <remarks>
/// 业务系统照抄时，把服务地址、API Key 放在自己的配置里。提交审批、撤回、待办、回调处理等对接代码在示例业务系统的任务里加到这个类库。
/// </remarks>
public sealed class ApprovalConnection : IDisposable
{
    /// <summary>
    /// 建立连接。
    /// </summary>
    /// <param name="serviceAddress">审批流服务的地址。</param>
    /// <param name="apiKey">接入应用的 API Key；只调服务信息时可以为 null。</param>
    public ApprovalConnection(Uri serviceAddress, string? apiKey)
    {
        Client = new ApprovalServiceClient(new ApprovalServiceClientOptions { BaseAddress = serviceAddress, ApiKey = apiKey });
    }

    /// <summary>
    /// SDK 客户端。
    /// </summary>
    public ApprovalServiceClient Client
    {
        get;
    }

    /// <summary>
    /// 检查能不能连上审批流服务，返回一句给人看的说明（示例系统启动时显示在状态栏）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>如“已连接审批流服务 0.1.0（接口 v1）”。</returns>
    /// <exception cref="ApprovalServiceException">连不上或服务返回错误。</exception>
    public async Task<string> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var info = await Client.GetInfoAsync(cancellationToken).ConfigureAwait(false);
        return $"已连接审批流服务 {info.Version}（接口 {info.ApiVersion}）";
    }

    /// <inheritdoc />
    public void Dispose() => Client.Dispose();
}
