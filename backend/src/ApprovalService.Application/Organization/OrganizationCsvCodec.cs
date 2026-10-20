using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Organization;

/// <summary>
/// 无第三方依赖的 UTF-8 CSV 导入格式；同一文件可含部门、人员和所属部门三种记录。
/// </summary>
public static class OrganizationCsvCodec
{
    /// <summary>
    /// CSV 模板列，空字段留空；首列 kind 为 department、person 或 membership。
    /// </summary>
    public const string Header = "kind,id,code,name,parentId,headPersonId,employeeNumber,phone,email,managerId,position,grade,gradeRank,isActive,personId,departmentId,isPrimary";

    /// <summary>
    /// 解析 CSV 字符串；结构错误作为可修正的校验错误返回。
    /// </summary>
    /// <param name="csv">UTF-8 解码后的 CSV 内容。</param>
    /// <returns>待校验的组织批次。</returns>
    public static OrganizationImportBatch Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var rows = ReadRows(csv);
        if (rows.Count == 0)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "组织 CSV 文件为空。");
        }

        var headers = rows[0].Select(value => value.Trim()).ToArray();
        var required = Header.Split(',');
        if (headers.Length != required.Length || !headers.SequenceEqual(required, StringComparer.Ordinal))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "组织 CSV 列名或顺序不正确，请下载最新模板。");
        }

        var batch = new OrganizationImportBatch();
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (row.Count != headers.Length)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"组织 CSV 第 {index + 1} 行列数不正确。");
            }

            switch (row[0].Trim())
            {
                case "department":
                    batch.Departments.Add(new DepartmentEntity
                    {
                        Id = row[1].Trim(),
                        Code = row[2].Trim(),
                        Name = row[3].Trim(),
                        ParentId = EmptyAsNull(row[4]),
                        HeadPersonId = EmptyAsNull(row[5]),
                    });
                    break;
                case "person":
                    if (!int.TryParse(row[12], out var gradeRank) ||
                        !bool.TryParse(row[13], out var isActive))
                    {
                        throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                            $"组织 CSV 第 {index + 1} 行职级序数或在职状态不正确。");
                    }

                    batch.People.Add(new PersonEntity
                    {
                        Id = row[1].Trim(),
                        Name = row[3].Trim(),
                        EmployeeNumber = row[6].Trim(),
                        Phone = EmptyAsNull(row[7]),
                        Email = EmptyAsNull(row[8]),
                        ManagerId = EmptyAsNull(row[9]),
                        Position = EmptyAsNull(row[10]),
                        Grade = EmptyAsNull(row[11]),
                        GradeRank = gradeRank,
                        IsActive = isActive,
                    });
                    break;
                case "membership":
                    if (!bool.TryParse(row[16], out var isPrimary))
                    {
                        throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                            $"组织 CSV 第 {index + 1} 行主部门标记不正确。");
                    }

                    batch.Memberships.Add(new PersonDepartmentEntity
                    {
                        PersonId = row[14].Trim(),
                        DepartmentId = row[15].Trim(),
                        IsPrimary = isPrimary,
                    });
                    break;
                default:
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                        $"组织 CSV 第 {index + 1} 行记录类型不正确。");
            }
        }

        return batch;
    }

    /// <summary>
    /// 生成供管理后台下载的空模板。
    /// </summary>
    /// <returns>包含 UTF-8 BOM 和列名的 CSV 文本。</returns>
    public static string Template() => "\uFEFF" + Header + "\r\n";

    /// <summary>
    /// 按 RFC 4180 的双引号规则读取记录，保留引号内逗号和换行。
    /// </summary>
    /// <param name="csv">CSV 文本。</param>
    /// <returns>按行拆分的字段。</returns>
    private static List<List<string>> ReadRows(string csv)
    {
        var result = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (character == '"')
            {
                if (quoted && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                row.Add(field.ToString());
                field.Clear();
                result.Add(row);
                row = [];
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                {
                    index++;
                }
            }
            else
            {
                field.Append(character);
            }
        }

        if (quoted)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "组织 CSV 存在未闭合的引号。");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            result.Add(row);
        }

        if (result.Count > 0 && result[0].Count > 0)
        {
            result[0][0] = result[0][0].TrimStart('\uFEFF');
        }

        return result;
    }

    /// <summary>
    /// 把空字段解释为可空关系。
    /// </summary>
    /// <param name="value">原始字段。</param>
    /// <returns>非空值或 null。</returns>
    private static string? EmptyAsNull(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
