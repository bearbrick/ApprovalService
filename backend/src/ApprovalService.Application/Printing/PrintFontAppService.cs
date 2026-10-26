using System.Security.Cryptography;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Printing;

/// <summary>
/// 组织管理员确认授权后上传字体，读取仅返回摘要。
/// </summary>
/// <param name="store">字体存储。</param>
/// <param name="clock">可信时间。</param>
/// <param name="printer">验证字体实际可用性。</param>
public sealed class PrintFontAppService(IPrintFontStore store, TimeProvider clock, IApprovalPrinter printer)
{
    /// <summary>
    /// 查询字体是否已准备好。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>安全摘要。</returns>
    public async Task<PrintFontResponse> GetAsync(CancellationToken cancellationToken = default) =>
        Describe(await store.ReadAsync(cancellationToken));

    /// <summary>
    /// 有界读取字体，校验后原子保存。
    /// </summary>
    /// <param name="content">原始请求体。</param>
    /// <param name="fileName">上传文件名。</param>
    /// <param name="licenseConfirmed">管理员确认合法内嵌授权。</param>
    /// <param name="actorId">可信管理员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>新字体摘要。</returns>
    public async Task<PrintFontResponse> UploadAsync(Stream content, string fileName, bool licenseConfirmed,
        string actorId, CancellationToken cancellationToken = default)
    {
        if (!licenseConfirmed || string.IsNullOrWhiteSpace(fileName) || fileName.Length > 120 ||
            fileName.Any(char.IsControl) || fileName.Contains('/') || fileName.Contains('\\') ||
            !fileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请选择 .ttf 字体并确认单位拥有 PDF 内嵌授权。");
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await content.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > 20 * 1024 * 1024)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "字体文件不能超过 20 MiB。");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        var bytes = buffer.ToArray();
        PrintFontValidator.Validate(bytes);
        printer.ValidateFont(bytes);
        var asset = new PrintFontAsset { FileName = fileName, Bytes = bytes, UploadedBy = actorId, UploadedAt = clock.GetUtcNow() };
        await store.WriteAsync(asset, cancellationToken);
        return Describe(asset);
    }

    /// <summary>
    /// 去掉字体原始内容。
    /// </summary>
    /// <param name="asset">当前字体。</param>
    /// <returns>摘要。</returns>
    private static PrintFontResponse Describe(PrintFontAsset? asset) => new()
    {
        Configured = asset is not null,
        FileName = asset?.FileName,
        Sha256 = asset is null ? null : Convert.ToHexStringLower(SHA256.HashData(asset.Bytes)),
        UploadedAt = asset?.UploadedAt,
    };
}
