using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Center;

/// <summary>
/// 为已授权当前查看人向所属应用申请一次性只读业务原单地址。
/// </summary>
/// <param name="center">与普通详情共用参与关系和应用范围的查询仓储。</param>
/// <param name="apps">应用配置仓储。</param>
/// <param name="hooks">双向签名并验签的出站钩子。</param>
/// <param name="clock">服务端时间来源。</param>
public sealed class CenterDocumentAppService(ICenterQueryRepository center, IClientAppRepository apps,
    IApprovalHookClient hooks, TimeProvider clock)
{
    /// <summary>
    /// 核对详情、外部身份、原单来源和期限后，仅在本次响应返回临时 URL。
    /// </summary>
    /// <param name="scope">服务端可信登录范围。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>未经持久化的短时只读原单地址。</returns>
    public async Task<CenterDocumentUrlResponse> GetAsync(CenterScope scope, string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (scope is null || string.IsNullOrWhiteSpace(scope.PersonId) ||
            (scope.ClientAppCode is not null && string.IsNullOrWhiteSpace(scope.ClientAppCode)))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "登录身份不完整，请重新登录。");
        }
        if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 100)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批单标识不正确。");
        }
        var record = await center.FindAsync(scope, instanceId, cancellationToken) ??
            throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在或当前人员无权查看。");
        var detail = CenterProjection.Detail(record, scope.PersonId);
        if (detail.Layout.Mode != "embed" || string.IsNullOrWhiteSpace(detail.Layout.EmbedPageCode))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "当前流程未配置业务原单展示。");
        }
        var app = await apps.FindByCodeAsync(record.Instance.ClientAppCode, cancellationToken);
        if (app is not { IsEnabled: true } || string.IsNullOrWhiteSpace(app.DocumentUrl))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "所属业务系统未启用原单展示。");
        }
        var externalId = await center.ResolveUniqueExternalPersonIdAsync(app.Code, scope.PersonId, cancellationToken);
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "当前人员没有唯一的业务系统身份绑定。");
        }
        string[] origins;
        try
        {
            origins = JsonSerializer.Deserialize<string[]>(app.AllowedDocumentOriginsJson) ?? [];
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务原单来源配置不正确。");
        }
        if (origins.Length is < 1 or > 20 || origins.Any(origin => !DocumentOrigin.IsValid(origin)))
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务原单来源配置不正确。");
        }
        var paths = detail.FieldDefinitions.SelectMany(field => field.Children.Count == 0
            ? [field.Key] : new[] { field.Key }.Concat(field.Children.Select(child => field.Key + "." + child.Key)))
            .Distinct(StringComparer.Ordinal).ToArray();
        var requestId = Guid.NewGuid().ToString("N");
        var raw = await hooks.InvokeAsync(app, app.DocumentUrl, new DocumentHookRequest
        {
            RequestId = requestId,
            InstanceId = record.Instance.Id,
            FlowCode = record.Instance.FlowCode,
            BusinessKey = record.Instance.BusinessKey,
            EmbedPageCode = detail.Layout.EmbedPageCode,
            ViewerExternalId = externalId,
            VisibleFieldPaths = paths,
        }, requestId, cancellationToken);
        DocumentHookResponse? result;
        try
        {
            result = raw.Deserialize<DocumentHookResponse>(ApprovalJson.Options);
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务原单响应格式不正确。");
        }
        var now = clock.GetUtcNow();
        if (result is null || result.Url.Length is < 1 or > 2048 ||
            !Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || result.ExpiresAt <= now || result.ExpiresAt > now.AddMinutes(5) ||
            !origins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务原单地址、来源或有效期不正确。");
        }
        return new CenterDocumentUrlResponse { Url = result.Url, ExpiresAt = result.ExpiresAt };
    }
}
