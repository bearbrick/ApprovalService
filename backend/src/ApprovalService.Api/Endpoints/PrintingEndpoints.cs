using System.Security.Claims;
using ApprovalService.Api.Endpoints.Center;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Printing;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 人员 JWT 打印和本地组织管理员上传字体的边界。
/// </summary>
internal static class PrintingEndpoints
{
    /// <summary>
    /// 登记打印与字体管理路由。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>同一路由登记器。</returns>
    public static IEndpointRouteBuilder MapPrintingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(PrintingRoutes.Instance, PrintAsync);
        app.MapGet(PrintingRoutes.Font, (PrintFontAppService service, CancellationToken cancellationToken) =>
            service.GetAsync(cancellationToken)).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(PrintingRoutes.Font, UploadAsync).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }

    /// <summary>
    /// 从 JWT 取当前人员，不接受请求正文或 API Key 冒充打印人。
    /// </summary>
    /// <param name="http">请求。</param>
    /// <param name="id">审批单 ID。</param>
    /// <param name="service">打印用例。</param>
    /// <param name="configuration">部署可信地址配置。</param>
    /// <param name="clients">接入应用 API Key 验证。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>不缓存的 PDF。</returns>
    private static async Task<IResult> PrintAsync(HttpContext http, string id, ApprovalPrintAppService service,
        IConfiguration configuration, ClientAppAppService clients, CancellationToken cancellationToken)
    {
        var configured = configuration["ApprovalService:Printing:PublicBaseUrl"];
        var url = string.Empty;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var address) ||
                address.Scheme is not ("https" or "http") || address.UserInfo.Length > 0 ||
                address.Query.Length > 0 || address.Fragment.Length > 0)
            {
                throw new InvalidOperationException("打印二维码公共地址配置不正确。");
            }
            url = configured.TrimEnd('/') + "/#/center/approvals/" + Uri.EscapeDataString(id);
        }
        var apiKey = http.Request.Headers[HeaderNames.ApiKey].ToString();
        byte[] pdf;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            // 同时携带 JWT 和 API Key 时明确按 API Key 归档，不混合两种权限。
            var client = await clients.AuthenticateAsync(apiKey, cancellationToken);
            pdf = await service.PrintForAppAsync(client.Code, client.Name, id, url, cancellationToken);
        }
        else
        {
            pdf = await service.PrintAsync(CenterQueryEndpoints.Scope(http), id, url, cancellationToken);
        }
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(pdf, "application/pdf", "approval.pdf");
    }

    /// <summary>
    /// 上传字体原始字节并确认合法授权，文件名不参与存储路径。
    /// </summary>
    /// <param name="http">已认证请求。</param>
    /// <param name="fileName">字体文件名。</param>
    /// <param name="licenseConfirmed">明确确认拥有授权。</param>
    /// <param name="service">字体上传用例。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>字体安全摘要。</returns>
    private static Task<PrintFontResponse> UploadAsync(HttpContext http, string fileName, bool licenseConfirmed,
        PrintFontAppService service, CancellationToken cancellationToken) => service.UploadAsync(http.Request.Body,
            fileName, licenseConfirmed, http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。"), cancellationToken);
}
