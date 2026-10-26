using System.Buffers.Binary;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Printing;

/// <summary>
/// 在 PDF 解析前验证上传 TrueType 表目录、边界和字体内嵌权限。
/// </summary>
public static class PrintFontValidator
{
    /// <summary>
    /// 检查独立 TrueType 字体，不接受字体集合或禁止内嵌的文件。
    /// </summary>
    /// <param name="bytes">上传内容。</param>
    public static void Validate(byte[] bytes)
    {
        if (bytes.Length < 64 || bytes.Length > 20 * 1024 * 1024 ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes) != 0x00010000)
        {
            throw Invalid();
        }
        var count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        if (count is < 1 or > 256 || 12 + count * 16 > bytes.Length)
        {
            throw Invalid();
        }
        var tags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.AsSpan(12 + index * 16, 16);
            var tag = System.Text.Encoding.ASCII.GetString(entry[..4]);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            if (!tags.Add(tag) || offset > bytes.Length || length > bytes.Length - offset)
            {
                throw Invalid();
            }
            if (tag == "OS/2")
            {
                if (length < 10)
                {
                    throw Invalid();
                }
                var rights = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan((int)offset + 8));
                // 拒绝受限、禁止子集和仅位图嵌入；管理员仍须确认自己持有合法授权。
                if ((rights & (0x0002 | 0x0100 | 0x0200)) != 0)
                {
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed, "字体授权标志不允许当前 PDF 内嵌方式，请上传允许嵌入的字体。");
                }
            }
        }
        if (!new[] { "cmap", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "name", "OS/2" }.All(tags.Contains))
        {
            throw Invalid();
        }
    }

    /// <summary>
    /// 返回格式错误，不把字体解析细节暴露给用户。
    /// </summary>
    /// <returns>可修正错误。</returns>
    private static UserFriendlyException Invalid() => new(ErrorCodes.ValidationFailed,
        "请上传完整的独立 TrueType 字体（.ttf），最大 20 MiB；不支持字体集合和 OpenType CFF。");
}

/// <summary>
/// 授权字体的持久化边界，不发布或下载字体原文件。
/// </summary>
public interface IPrintFontStore
{
    /// <summary>
    /// 读取当前上传字体。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>字体或空。</returns>
    Task<PrintFontAsset?> ReadAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// 原子替换当前字体及其上传审计元数据。
    /// </summary>
    /// <param name="asset">已验证字体。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>保存完成。</returns>
    Task WriteAsync(PrintFontAsset asset, CancellationToken cancellationToken = default);
}

/// <summary>
/// 单位提供的字体内容及上传来源。
/// </summary>
public sealed class PrintFontAsset
{
    /// <summary>
    /// 字体原文件名。
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>
    /// 字体内容。
    /// </summary>
    public byte[] Bytes { get; set; } = [];
    /// <summary>
    /// 上传管理员。
    /// </summary>
    public string UploadedBy { get; set; } = string.Empty;
    /// <summary>
    /// 上传时间。
    /// </summary>
    public DateTimeOffset UploadedAt
    {
        get; set;
    }
}
