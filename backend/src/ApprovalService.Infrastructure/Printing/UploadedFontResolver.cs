using System.Collections.Concurrent;
using System.Security.Cryptography;
using PdfSharp.Fonts;

namespace ApprovalService.Infrastructure.Printing;

/// <summary>
/// 以字体内容散列隔离上传版本，避免并发打印混用字体。
/// </summary>
internal sealed class UploadedFontResolver : IFontResolver
{
    /// <summary>
    /// 当前进程已经使用的字体内容；PDFsharp 按字体名缓存解析结果。
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]> Fonts = new();
    /// <summary>
    /// 保护进程级字体解析器首次登记。
    /// </summary>
    private static readonly object Gate = new();

    /// <summary>
    /// 登记已验证字体并取得内容寻址的字体名。
    /// </summary>
    /// <param name="bytes">上传字体。</param>
    /// <returns>不会与其他版本混用的字体名。</returns>
    public static string Register(byte[] bytes)
    {
        var name = "Approval-" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        lock (Gate)
        {
            GlobalFontSettings.FontResolver ??= new UploadedFontResolver();
            Fonts.TryAdd(name, bytes);
        }
        return name;
    }

    /// <inheritdoc />
    public byte[] GetFont(string faceName) => Fonts[faceName];

    /// <inheritdoc />
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        Fonts.ContainsKey(familyName) ? new FontResolverInfo(familyName, bold, italic) : null;
}
