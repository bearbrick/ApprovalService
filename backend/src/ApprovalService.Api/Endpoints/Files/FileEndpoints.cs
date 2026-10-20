using System.Security.Claims;
using ApprovalService.Application.Files;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ApprovalService.Api.Endpoints.Files;

/// <summary>
/// 文件接口只接受宿主鉴权中间件放入的身份，不读取可伪造的用户 ID 参数。
/// </summary>
internal static class FileEndpoints
{
    /// <summary>
    /// 登记上传和验权下载接口。
    /// </summary>
    /// <param name="app">路由登记器。</param>
    /// <returns>同一个路由登记器。</returns>
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // 请求体上限由应用层的可配置字节计数控制，允许部署单位将其调高于 Kestrel 默认值。
        app.MapPost(ApiRoutes.Files, UploadAsync).WithMetadata(new DisableRequestSizeLimitAttribute());
        app.MapGet(ApiRoutes.FileById, DownloadAsync);
        return app;
    }

    /// <summary>
    /// 把原始请求体交给应用服务；上传者只能来自可信身份。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <param name="fileName">上传文件显示名。</param>
    /// <param name="service">文件应用服务。</param>
    /// <param name="cancellationToken">请求取消信号。</param>
    /// <returns>上传结果。</returns>
    private static async Task<IResult> UploadAsync(HttpContext http, string? fileName, IFileAppService service,
        CancellationToken cancellationToken)
    {
        var userId = RequireVerifiedUser(http.User);
        var uploaded = await service.UploadAsync(new UploadFileInput
        {
            Content = http.Request.Body,
            FileName = fileName ?? string.Empty,
            UploaderId = userId,
        }, cancellationToken);
        return Results.Json(new FileUploadResponse(uploaded.Id, uploaded.FileName, uploaded.Size,
            uploaded.ContentType, uploaded.Sha256));
    }

    /// <summary>
    /// 验权后返回短时对象地址或服务转发的文件内容。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <param name="id">附件 ID。</param>
    /// <param name="service">文件应用服务。</param>
    /// <param name="cancellationToken">请求取消信号。</param>
    /// <returns>下载响应。</returns>
    private static async Task<IResult> DownloadAsync(HttpContext http, string id, IFileAppService service,
        CancellationToken cancellationToken)
    {
        var userId = RequireVerifiedUser(http.User);
        // 大文件先写到会自动删除的临时文件，避免按可配置上限占用整份内存。
        var temporary = Path.Combine(Path.GetTempPath(), "approval-download-" + Guid.NewGuid().ToString("N"));
        var buffer = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 80 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            var result = await service.DownloadAsync(id, userId, buffer, cancellationToken);
            if (result.DirectUrl is not null)
            {
                await buffer.DisposeAsync();
                return Results.Redirect(result.DirectUrl.ToString());
            }

            buffer.Position = 0;
            return Results.File(buffer, result.ContentType, result.FileName, enableRangeProcessing: true);
        }
        catch
        {
            await buffer.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// 未接入身份服务时默认拒绝；以后只从已验证的 ClaimsPrincipal 提取 ID。
    /// </summary>
    /// <param name="principal">宿主鉴权中间件建立的身份。</param>
    /// <returns>可信的用户 ID。</returns>
    private static string RequireVerifiedUser(ClaimsPrincipal principal)
    {
        var userId = principal.Identities.Where(identity => identity.IsAuthenticated)
            .Select(identity => identity.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录后再操作附件。");
        }

        return userId;
    }
}
