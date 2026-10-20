using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
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
    /// Office Open XML 是 ZIP 容器，必须包含有效清单、入口关系与对应文档主部件。
    /// </summary>
    /// <param name="content">已通过上传字节上限的可定位文件流。</param>
    /// <param name="extension">上传文件扩展名。</param>
    /// <returns>对应 Office 类型；结构不匹配时为空。</returns>
    private static string? DetectOfficeType(Stream content, string extension)
    {
        (string Part, string ContentType, string Root, string Namespace)? expected = extension switch
        {
            ".docx" => (Part: "word/document.xml", ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", Root: "document", Namespace: "http://schemas.openxmlformats.org/wordprocessingml/2006/main"),
            ".xlsx" => (Part: "xl/workbook.xml", ContentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Root: "workbook", Namespace: "http://schemas.openxmlformats.org/spreadsheetml/2006/main"),
            ".pptx" => (Part: "ppt/presentation.xml", ContentType: "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml", Root: "presentation", Namespace: "http://schemas.openxmlformats.org/presentationml/2006/main"),
            _ => null,
        };
        if (expected is null || !content.CanSeek)
        {
            return null;
        }

        try
        {
            content.Position = 0;
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var typesEntry = archive.GetEntry("[Content_Types].xml");
            var relationshipsEntry = archive.GetEntry("_rels/.rels");
            var mainEntry = archive.GetEntry(expected.Value.Part);
            if (typesEntry is null || relationshipsEntry is null || mainEntry is null)
            {
                return null;
            }

            XNamespace typesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace relationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
            var types = ReadPackageXml(typesEntry);
            var relationships = ReadPackageXml(relationshipsEntry);
            if (types.Root?.Name != typesNamespace + "Types" ||
                !types.Root.Elements(typesNamespace + "Override").Any(item =>
                    (string?)item.Attribute("PartName") == "/" + expected.Value.Part &&
                    (string?)item.Attribute("ContentType") == expected.Value.ContentType) ||
                relationships.Root?.Name != relationshipsNamespace + "Relationships" ||
                !relationships.Root.Elements(relationshipsNamespace + "Relationship").Any(item =>
                    (string?)item.Attribute("Type") ==
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" &&
                    (string?)item.Attribute("Target") is string target &&
                    target.TrimStart('/') == expected.Value.Part) ||
                !HasDocumentRoot(mainEntry, expected.Value.Root, expected.Value.Namespace))
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
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

    /// <summary>
    /// 有界读取包清单或入口关系，禁止文档类型定义和外部资源。
    /// </summary>
    /// <param name="entry">压缩包中的 XML 条目。</param>
    /// <returns>已解析的短 XML 文档。</returns>
    private static XDocument ReadPackageXml(ZipArchiveEntry entry)
    {
        const int maximumBytes = 256 * 1024;
        if (entry.Length is 0 or > maximumBytes)
        {
            throw new InvalidDataException("Office 包清单大小无效。");
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes,
        });
        return XDocument.Load(reader);
    }

    /// <summary>
    /// 完整读取主部件 XML，避免只靠文件名或开头标签接受伪造压缩包。
    /// </summary>
    /// <param name="entry">主部件压缩条目。</param>
    /// <param name="root">该 Office 类型的根元素名。</param>
    /// <param name="namespaceUri">该 Office 类型的 XML 命名空间。</param>
    /// <returns>主部件为有界且完整的预期 XML 时为真。</returns>
    private static bool HasDocumentRoot(ZipArchiveEntry entry, string root, string namespaceUri)
    {
        const int maximumBytes = 32 * 1024 * 1024;
        if (entry.Length is 0 or > maximumBytes)
        {
            return false;
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes,
        });
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != root ||
            reader.NamespaceURI != namespaceUri)
        {
            return false;
        }

        while (reader.Read())
        {
            // 读完整个主部件，确保后续 XML 损坏或超限不会被当成真实 Office 文件。
        }

        return true;
    }
}
