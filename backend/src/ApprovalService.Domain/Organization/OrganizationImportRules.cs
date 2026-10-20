namespace ApprovalService.Domain.Organization;

/// <summary>
/// 一批待导入的组织数据。
/// </summary>
public sealed class OrganizationImportBatch
{
    /// <summary>部门行。</summary>
    public List<DepartmentEntity> Departments { get; set; } = [];
    /// <summary>人员行。</summary>
    public List<PersonEntity> People { get; set; } = [];
    /// <summary>人员部门关系。</summary>
    public List<PersonDepartmentEntity> Memberships { get; set; } = [];
}

/// <summary>
/// 导入前的逐项问题。
/// </summary>
public sealed class OrganizationImportIssue
{
    /// <summary>问题字段路径。</summary>
    public string Field { get; set; } = string.Empty;
    /// <summary>中文问题说明。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 组织导入的完整性校验规则；一次返回全部可发现的问题。
/// </summary>
public static class OrganizationImportRules
{
    /// <summary>
    /// 校验工号、引用和人员及部门层级环。
    /// </summary>
    /// <param name="batch">待导入批次。</param>
    /// <param name="existingDepartments">库中现有部门。</param>
    /// <param name="existingPeople">库中现有人员。</param>
    /// <returns>按数据行排序的问题列表。</returns>
    public static IReadOnlyList<OrganizationImportIssue> Validate(OrganizationImportBatch batch,
        IReadOnlyCollection<DepartmentEntity> existingDepartments, IReadOnlyCollection<PersonEntity> existingPeople)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(existingDepartments);
        ArgumentNullException.ThrowIfNull(existingPeople);
        var issues = new List<OrganizationImportIssue>();
        var allDepartments = existingDepartments.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var allPeople = existingPeople.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var department in batch.Departments)
        {
            if (!string.IsNullOrWhiteSpace(department.Id))
            {
                allDepartments[department.Id] = department;
            }
        }

        foreach (var person in batch.People)
        {
            if (!string.IsNullOrWhiteSpace(person.Id))
            {
                allPeople[person.Id] = person;
            }
        }

        var importedEmployees = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < batch.People.Count; index++)
        {
            var person = batch.People[index];
            var path = $"people[{index}]";
            if (string.IsNullOrWhiteSpace(person.Id) || string.IsNullOrWhiteSpace(person.EmployeeNumber) ||
                string.IsNullOrWhiteSpace(person.Name))
            {
                Add(issues, path, "人员 ID、工号和姓名不能为空。");
            }

            if (!string.IsNullOrWhiteSpace(person.EmployeeNumber))
            {
                if (importedEmployees.TryGetValue(person.EmployeeNumber, out var previous))
                {
                    Add(issues, path + ".employeeNumber", $"工号与第 {previous + 1} 行重复。");
                }
                else
                {
                    importedEmployees[person.EmployeeNumber] = index;
                }

                if (existingPeople.Any(item => item.EmployeeNumber.Equals(person.EmployeeNumber,
                        StringComparison.OrdinalIgnoreCase) && item.Id != person.Id))
                {
                    Add(issues, path + ".employeeNumber", "工号已被其他人员使用。");
                }
            }

            if (person.ManagerId is not null && !allPeople.ContainsKey(person.ManagerId))
            {
                Add(issues, path + ".managerId", "直属上级不存在。");
            }
        }

        for (var index = 0; index < batch.Departments.Count; index++)
        {
            var department = batch.Departments[index];
            var path = $"departments[{index}]";
            if (string.IsNullOrWhiteSpace(department.Id) || string.IsNullOrWhiteSpace(department.Code) ||
                string.IsNullOrWhiteSpace(department.Name))
            {
                Add(issues, path, "部门 ID、编码和名称不能为空。");
            }

            if (department.ParentId is not null && !allDepartments.ContainsKey(department.ParentId))
            {
                Add(issues, path + ".parentId", "上级部门不存在。");
            }

            if (department.HeadPersonId is not null && !allPeople.ContainsKey(department.HeadPersonId))
            {
                Add(issues, path + ".headPersonId", "部门负责人不存在。");
            }
        }

        for (var index = 0; index < batch.Memberships.Count; index++)
        {
            var membership = batch.Memberships[index];
            if (!allPeople.ContainsKey(membership.PersonId) || !allDepartments.ContainsKey(membership.DepartmentId))
            {
                Add(issues, $"memberships[{index}]", "人员或部门不存在。");
            }
        }

        // 对每条导入行沿父链前进；每条路径一旦重复即指出当前行，不因另一行错误中断校验。
        for (var index = 0; index < batch.People.Count; index++)
        {
            if (HasCycle(batch.People[index].Id, allPeople, item => item.ManagerId))
            {
                Add(issues, $"people[{index}].managerId", "直属上级关系形成环。");
            }
        }

        for (var index = 0; index < batch.Departments.Count; index++)
        {
            if (HasCycle(batch.Departments[index].Id, allDepartments, item => item.ParentId))
            {
                Add(issues, $"departments[{index}].parentId", "部门上下级关系形成环。");
            }
        }

        return issues;
    }

    /// <summary>
    /// 从指定节点沿上级关系查找环。
    /// </summary>
    /// <typeparam name="T">层级实体类型。</typeparam>
    /// <param name="startId">起点 ID。</param>
    /// <param name="items">合并后的实体字典。</param>
    /// <param name="parent">读取上级 ID 的函数。</param>
    /// <returns>路径中存在环为真。</returns>
    private static bool HasCycle<T>(string startId, IReadOnlyDictionary<string, T> items, Func<T, string?> parent)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = startId;
        while (items.TryGetValue(current, out var item))
        {
            if (!visited.Add(current))
            {
                return true;
            }

            current = parent(item) ?? string.Empty;
        }

        return false;
    }

    /// <summary>
    /// 追加一个校验问题。
    /// </summary>
    /// <param name="issues">问题列表。</param>
    /// <param name="field">字段路径。</param>
    /// <param name="message">中文提示。</param>
    private static void Add(List<OrganizationImportIssue> issues, string field, string message) =>
        issues.Add(new OrganizationImportIssue { Field = field, Message = message });
}
