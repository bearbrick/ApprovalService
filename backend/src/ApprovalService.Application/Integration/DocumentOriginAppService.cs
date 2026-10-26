using System.Text.Json;
using ApprovalService.Domain.Integration;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 只从受控应用配置生成业务原单 iframe 的 CSP 来源集合。
/// </summary>
/// <param name="apps">应用配置仓储。</param>
public sealed class DocumentOriginAppService(IClientAppRepository apps)
{
    /// <summary>
    /// 读取某个已启用应用的安全来源；损坏配置不会进入 CSP。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>规范且不重复的 HTTPS 来源。</returns>
    public async Task<IReadOnlyList<string>> GetForAppAsync(string code,
        CancellationToken cancellationToken = default)
    {
        var app = await apps.FindByCodeAsync(code, cancellationToken);
        return app is { IsEnabled: true } ? Parse(app.AllowedDocumentOriginsJson) : [];
    }

    /// <summary>
    /// 为普通本地入口汇总所有启用应用的安全来源。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>所有启用应用的 HTTPS 来源并集。</returns>
    public async Task<IReadOnlyList<string>> GetForLocalHomeAsync(CancellationToken cancellationToken = default)
    {
        var appsList = await apps.ListAsync(cancellationToken);
        return appsList.Where(app => app.IsEnabled).SelectMany(app => Parse(app.AllowedDocumentOriginsJson))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(origin => origin, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 配置意外损坏时返回空来源，不把任意内容拼进 CSP。
    /// </summary>
    /// <param name="json">持久来源列表。</param>
    /// <returns>安全来源。</returns>
    private static IReadOnlyList<string> Parse(string json)
    {
        try
        {
            var origins = JsonSerializer.Deserialize<string[]>(json);
            return origins is { Length: <= 20 } && origins.All(DocumentOrigin.IsValid)
                ? origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
