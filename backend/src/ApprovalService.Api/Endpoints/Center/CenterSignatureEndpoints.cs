using ApprovalService.Application.Center;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints.Center;

/// <summary>
/// 人员 JWT 专用的手写签名上传和验权图片读取。
/// </summary>
internal static class CenterSignatureEndpoints
{
    /// <summary>
    /// 登记签名资产路由。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>同一路由登记器。</returns>
    public static IEndpointRouteBuilder MapCenterSignatureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(CenterRoutes.Signatures, UploadAsync).RequireAuthorization();
        app.MapGet(CenterRoutes.PersonalSignature, PersonalAsync).RequireAuthorization();
        app.MapGet(CenterRoutes.SignatureById, ReadAsync).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// 读取原始 PNG，个人签名仅限本地会话保存。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="personal">是否作为本人可重复选择的签名。</param>
    /// <param name="service">签名用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>资产元数据。</returns>
    private static Task<CenterSignatureResponse> UploadAsync(HttpContext http, bool? personal,
        SignatureAppService service, CancellationToken cancellationToken)
    {
        if (!string.Equals(http.Request.ContentType?.Split(';')[0], "image/png", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "签名请求须使用 image/png 内容类型。");
        }
        return service.UploadAsync(CenterQueryEndpoints.Scope(http), http.Request.Body, personal == true,
            cancellationToken);
    }

    /// <summary>
    /// 返回本人最近的个人签名元数据，未设置时响应空。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="service">签名用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>个人签名元数据。</returns>
    private static Task<CenterSignatureResponse?> PersonalAsync(HttpContext http, SignatureAppService service,
        CancellationToken cancellationToken) => service.LatestPersonalAsync(CenterQueryEndpoints.Scope(http),
        cancellationToken);

    /// <summary>
    /// 本人或已授权单据参与者读取冻结的 PNG。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="id">签名资产 ID。</param>
    /// <param name="instanceId">可选审批单 ID。</param>
    /// <param name="service">签名用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>不缓存的 PNG 内容。</returns>
    private static async Task<IResult> ReadAsync(HttpContext http, string id, string? instanceId,
        SignatureAppService service, CancellationToken cancellationToken)
    {
        var png = await service.ReadPngAsync(CenterQueryEndpoints.Scope(http), id, instanceId, cancellationToken);
        return Results.File(png, "image/png");
    }
}
