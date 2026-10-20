using System.IO.Compression;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Files;

/// <summary>
/// 用文件签名而非浏览器声明的媒体类型识别允许上传的附件。
/// </summary>
public static class FileTypeDetector
{
    /// <summary>
    /// 清除客户端传入的目录并检查显示文件名。
    /// </summary>
    /// <param name="fileName">未经信任的上传文件名。</param>
    /// <returns>仅含最后一个路径段的显示名。</returns>
    public static string NormalizeName(string fileName)
    {
        var safeName = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        if (safeName.Length is 0 or > 255 || safeName.Any(char.IsControl) || safeName is "." or "..")
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "附件文件名不正确。");
        }

        return safeName;
    }

    /// <summary>
    /// 按内容识别图片、PDF 或 Office Open XML，并要求扩展名与实际内容一致。
    /// </summary>
    /// <param name="signature">文件头部签名字节。</param>
    /// <param name="safeName">已清理路径的显示文件名。</param>
    /// <param name="fullContent">从零位置读取、仅供检查 Office 压缩包结构的完整流。</param>
    /// <returns>可信媒体类型。</returns>
    public static string DetectContentType(ReadOnlySpan<byte> signature, string safeName, Stream fullContent)
    {
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        string? contentType = null;
        string[] validExtensions = [];
        if (signature.Length >= 5 && signature[..5].SequenceEqual("%PDF-"u8))
        {
            contentType = "application/pdf";
            validExtensions = [".pdf"];
        }
        else if (signature.Length >= 8 && signature[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            contentType = "image/png";
            validExtensions = [".png"];
        }
        else if (signature.Length >= 3 && signature[..3].SequenceEqual(new byte[] { 255, 216, 255 }))
        {
            contentType = "image/jpeg";
            validExtensions = [".jpg", ".jpeg"];
        }
        else if (signature.Length >= 6 &&
            (signature[..6].SequenceEqual("GIF87a"u8) || signature[..6].SequenceEqual("GIF89a"u8)))
        {
            contentType = "image/gif";
            validExtensions = [".gif"];
        }
        else if (signature.Length >= 12 && signature[..4].SequenceEqual("RIFF"u8) &&
            signature.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            contentType = "image/webp";
            validExtensions = [".webp"];
        }
        else if (signature.Length >= 4 && signature[..4].SequenceEqual("PK\x03\x04"u8))
        {
            contentType = DetectOfficeType(fullContent, extension);
            validExtensions = [extension];
        }

        if (contentType is null || !validExtensions.Contains(extension, StringComparer.Ordinal))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "附件类型不支持，或文件扩展名与实际内容不一致。允许 PDF、PNG、JPEG、GIF、WebP、DOCX、XLSX、PPTX。");
        }

        return contentType;
    }

    /// <summary>
    /// Office Open XML 是 ZIP 容器，必须同时包含通用清单与对应文档主部件。
    /// </summary>
    /// <param name="content">已通过上传字节上限的可定位文件流。</param>
    /// <param name="extension">上传文件扩展名。</param>
    /// <returns>对应 Office 类型；结构不匹配时为空。</returns>
    private static string? DetectOfficeType(Stream content, string extension)
    {
        var part = extension switch
        {
            ".docx" => "word/document.xml",
            ".xlsx" => "xl/workbook.xml",
            ".pptx" => "ppt/presentation.xml",
            _ => null,
        };
        if (part is null || !content.CanSeek)
        {
            return null;
        }

        try
        {
            content.Position = 0;
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.GetEntry("[Content_Types].xml") is null || archive.GetEntry(part) is null)
            {
                return null;
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return extension switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        };
    }
}
