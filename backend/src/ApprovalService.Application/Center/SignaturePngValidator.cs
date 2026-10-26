using System.Buffers.Binary;
using System.IO.Compression;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Center;

/// <summary>
/// 对手写 PNG 校验完整分块、校验和及受限像素流，不仅检查文件头。
/// </summary>
public static class SignaturePngValidator
{
    /// <summary>
    /// PNG 文件签名。
    /// </summary>
    private static readonly byte[] Magic = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// 检查浏览器画布可生成的 8 位 RGB 或 RGBA、非隔行 PNG。
    /// </summary>
    /// <param name="png">最多 2 MiB 的完整文件内容。</param>
    public static void Validate(ReadOnlySpan<byte> png)
    {
        if (png.Length < 8 + 25 + 12 || !png[..8].SequenceEqual(Magic))
        {
            Invalid();
        }

        var offset = 8;
        var seenHeader = false;
        var seenData = false;
        var seenEnd = false;
        var dataEnded = false;
        var width = 0;
        var height = 0;
        var channels = 0;
        using var compressed = new MemoryStream();
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
            {
                Invalid();
            }
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            if (length > png.Length - offset - 12)
            {
                Invalid();
            }
            var size = checked((int)length);
            var kind = png.Slice(offset + 4, 4);
            var content = png.Slice(offset + 8, size);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8 + size, 4));
            if (Crc32(png.Slice(offset + 4, 4 + size)) != expectedCrc || seenEnd)
            {
                Invalid();
            }

            if (kind.SequenceEqual("IHDR"u8))
            {
                if (seenHeader || offset != 8 || size != 13)
                {
                    Invalid();
                }
                var rawWidth = BinaryPrimitives.ReadUInt32BigEndian(content[..4]);
                var rawHeight = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4, 4));
                if (rawWidth is < 1 or > 4096 || rawHeight is < 1 or > 4096)
                {
                    Invalid();
                }
                width = (int)rawWidth;
                height = (int)rawHeight;
                channels = content[9] == 6 ? 4 : content[9] == 2 ? 3 : 0;
                if (width is < 1 or > 4096 || height is < 1 or > 4096 || content[8] != 8 ||
                    channels == 0 || content[10] != 0 || content[11] != 0 || content[12] != 0)
                {
                    Invalid();
                }
                seenHeader = true;
            }
            else if (kind.SequenceEqual("IDAT"u8))
            {
                if (!seenHeader || dataEnded || compressed.Length + size > 2 * 1024 * 1024)
                {
                    Invalid();
                }
                compressed.Write(content);
                seenData = true;
            }
            else if (kind.SequenceEqual("IEND"u8))
            {
                if (!seenHeader || !seenData || size != 0)
                {
                    Invalid();
                }
                seenEnd = true;
            }
            else
            {
                if (!seenHeader || (kind[0] & 0x20) == 0)
                {
                    // 未知关键分块会改变解码语义；仅接受无关的辅助分块。
                    Invalid();
                }
                dataEnded |= seenData;
            }
            offset += 12 + size;
        }

        if (!seenEnd || offset != png.Length)
        {
            Invalid();
        }
        // 限制解码后字节数，防止合法压缩流将小文件膨胀成内存炸弹。
        var rowSize = checked(width * channels + 1);
        var required = checked(rowSize * height);
        if (required > 64 * 1024 * 1024)
        {
            Invalid();
        }
        compressed.Position = 0;
        try
        {
            using var decoder = new ZLibStream(compressed, CompressionMode.Decompress, true);
            var row = new byte[rowSize];
            for (var index = 0; index < height; index++)
            {
                decoder.ReadExactly(row);
                if (row[0] > 4)
                {
                    Invalid();
                }
            }
            if (decoder.ReadByte() != -1)
            {
                Invalid();
            }
        }
        catch (InvalidDataException)
        {
            Invalid();
        }
        catch (EndOfStreamException)
        {
            Invalid();
        }
    }

    /// <summary>
    /// 计算 PNG 标准 CRC-32。
    /// </summary>
    /// <param name="bytes">分块类型与内容。</param>
    /// <returns>校验和。</returns>
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
            }
        }
        return ~crc;
    }

    /// <summary>
    /// 向调用方统一返回可修正的图片错误。
    /// </summary>
    private static void Invalid() => throw new UserFriendlyException(ErrorCodes.ValidationFailed,
        "签名图片必须是完整的 PNG，尺寸不超过 4096×4096，且使用 8 位 RGB 或 RGBA 非隔行编码。");
}
