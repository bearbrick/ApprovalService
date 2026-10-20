using System.Globalization;
using System.Text;
using ApprovalService.Application.Organization;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using MiniExcelLibs;

namespace ApprovalService.Infrastructure.Organization;

/// <summary>
/// 把 Excel 工作表转换为与 CSV 相同的组织导入格式。
/// </summary>
public static class OrganizationExcelCodec
{
    /// <summary>
    /// 单次导入允许的最大记录数。
    /// </summary>
    public const int MaximumRows = 10000;

    /// <summary>
    /// 解压后组织字段可累计生成的最大字符数。
    /// </summary>
    public const int MaximumCharacters = 10 * 1024 * 1024;

    /// <summary>
    /// 读取 Excel 首张工作表并复用组织 CSV 的字段解析与校验格式。
    /// </summary>
    /// <param name="stream">可读取的 Excel 文件流。</param>
    /// <returns>待校验的组织批次。</returns>
    public static OrganizationImportBatch Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var headers = OrganizationCsvCodec.Header.Split(',');
        var csv = new StringBuilder(OrganizationCsvCodec.Header).Append("\r\n");
        var count = 0;
        try
        {
            foreach (var item in stream.Query(useHeaderRow: true, excelType: ExcelType.XLSX))
            {
                if (++count > MaximumRows)
                {
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                        $"组织 Excel 文件最多允许 {MaximumRows} 行记录。");
                }

                if (item is not IDictionary<string, object> row ||
                    row.Keys.Count != headers.Length ||
                    !headers.All(row.ContainsKey))
                {
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                        "组织 Excel 列名不正确，请下载最新模板。");
                }

                // 单元格统一转为 CSV 字段，使两个文件格式采用相同的空值、布尔值和记录类型规则。
                for (var index = 0; index < headers.Length; index++)
                {
                    var value = Convert.ToString(row[headers[index]], CultureInfo.InvariantCulture);
                    if (value is { Length: > MaximumCharacters } ||
                        (value?.Length ?? 0) > MaximumCharacters - csv.Length)
                    {
                        throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                            "组织 Excel 解压后内容过大，请拆分文件导入。");
                    }

                    if (index > 0)
                    {
                        csv.Append(',');
                    }

                    csv.Append(Escape(value));
                }

                csv.Append("\r\n");
                if (csv.Length > MaximumCharacters)
                {
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                        "组织 Excel 解压后内容过大，请拆分文件导入。");
                }
            }
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or IOException)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "组织 Excel 文件格式不正确。");
        }

        return OrganizationCsvCodec.Parse(csv.ToString());
    }

    /// <summary>
    /// 生成与 CSV 导入字段一致的 Excel 空模板。
    /// </summary>
    /// <returns>可下载的 XLSX 字节。</returns>
    public static byte[] Template()
    {
        var row = OrganizationCsvCodec.Header.Split(',')
            .ToDictionary(header => header, _ => (object?)null, StringComparer.Ordinal);
        using var stream = new MemoryStream();
        stream.SaveAs(new[] { row });
        return stream.ToArray();
    }

    /// <summary>
    /// 为复用 CSV 解析器转义 Excel 单元格中的逗号、引号和换行。
    /// </summary>
    /// <param name="value">单元格文本。</param>
    /// <returns>一个合法 CSV 字段。</returns>
    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : text;
    }
}
