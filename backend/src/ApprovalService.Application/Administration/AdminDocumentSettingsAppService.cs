using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 管理员维护业务原单签名钩子和受信子页面来源。
/// </summary>
/// <param name="repository">接入应用仓储。</param>
/// <param name="transportPolicy">部署级内网 HTTP 钩子开关。</param>
public sealed class AdminDocumentSettingsAppService(IClientAppRepository repository,
    IntegrationTransportPolicy transportPolicy)
{
    /// <summary>
    /// 读取应用当前原单设置，不返回任何签名密钥。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>原单设置。</returns>
    public async Task<AdminDocumentSettingsResponse> GetAsync(string code,
        CancellationToken cancellationToken = default)
    {
        var app = await repository.FindByCodeAsync(code, cancellationToken) ??
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在。");
        return new AdminDocumentSettingsResponse
        {
            Code = app.Code,
            DocumentUrl = app.DocumentUrl,
            AllowedDocumentOrigins = JsonSerializer.Deserialize<string[]>(app.AllowedDocumentOriginsJson) ?? [],
        };
    }

    /// <summary>
    /// 校验完整配置并在同一事务保存脱敏审计。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="input">目标配置及原因。</param>
    /// <param name="actorId">当前组织管理员人员 ID。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已保存的规范配置。</returns>
    public async Task<AdminDocumentSettingsResponse> SetAsync(string code, UpdateAdminDocumentSettingsRequest input,
        string actorId, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
        }
        var reason = input.Reason.Trim();
        if (reason.Length is < 2 or > 200)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请填写 2 到 200 个字符的变更原因。");
        }
        var documentUrl = string.IsNullOrWhiteSpace(input.DocumentUrl) ? null : input.DocumentUrl.Trim();
        if (documentUrl is not null && (documentUrl.Length > 2048 ||
            !Uri.TryCreate(documentUrl, UriKind.Absolute, out var hookUri) ||
            (hookUri.Scheme != Uri.UriSchemeHttps &&
                !(transportPolicy.AllowHttpUrls && hookUri.Scheme == Uri.UriSchemeHttp)) ||
            hookUri.UserInfo.Length != 0))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "原单钩子必须是 HTTPS 完整地址；内网 HTTP 需要部署时显式开启。");
        }
        if (input.AllowedDocumentOrigins is null || input.AllowedDocumentOrigins.Count > 20)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "原单来源最多 20 项。");
        }
        var origins = new List<string>();
        foreach (var source in input.AllowedDocumentOrigins)
        {
            var candidate = source?.Trim() ?? string.Empty;
            if (candidate.Length is < 8 or > 256 || candidate.Contains('*') ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
                !string.Equals(candidate, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "原单来源须为完整 HTTPS 网站来源，不能包含路径或通配符。");
            }
            if (!origins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
            {
                origins.Add(uri.GetLeftPart(UriPartial.Authority));
            }
        }
        var app = await repository.FindByCodeAsync(code, cancellationToken) ??
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在。");
        var json = JsonSerializer.Serialize(origins);
        if (!await repository.UpdateDocumentSettingsAsync(app.Code, app.DocumentUrl,
            app.AllowedDocumentOriginsJson, documentUrl, json, actorId, reason, occurredAt, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "接入应用设置已变化，请刷新后重试。");
        }
        return new AdminDocumentSettingsResponse
        {
            Code = app.Code,
            DocumentUrl = documentUrl,
            AllowedDocumentOrigins = origins,
        };
    }
}
