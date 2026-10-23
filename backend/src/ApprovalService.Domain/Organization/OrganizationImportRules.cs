namespace ApprovalService.Domain.Organization;

/// <summary>
/// 一批待导入的组织数据。
/// </summary>
public sealed class OrganizationImportBatch
{
    /// <summary>
    /// 部门行。
    /// </summary>
    public List<DepartmentEntity> Departments { get; set; } = [];
    /// <summary>
    /// 人员行。
    /// </summary>
    public List<PersonEntity> People { get; set; } = [];
    /// <summary>
    /// 人员部门关系。
    /// </summary>
    public List<PersonDepartmentEntity> Memberships { get; set; } = [];
    /// <summary>
    /// 外部人员标识绑定。
    /// </summary>
    public List<ExternalPersonBindingEntity> PersonBindings { get; set; } = [];
    /// <summary>
    /// 外部部门标识绑定。
    /// </summary>
    public List<ExternalDepartmentBindingEntity> DepartmentBindings { get; set; } = [];
}

/// <summary>
/// 导入前的逐项问题。
/// </summary>
public sealed class OrganizationImportIssue
{
    /// <summary>
    /// 问题字段路径。
    /// </summary>
    public string Field { get; set; } = string.Empty;
    /// <summary>
    /// 中文问题说明。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 同一事务内校验与保存组织变更的结果。
/// </summary>
public sealed class OrganizationImportWriteResult
{
    /// <summary>
    /// 当前数据库快照下的校验问题。
    /// </summary>
    public IReadOnlyList<OrganizationImportIssue> Issues { get; set; } = [];
    /// <summary>
    /// 需要交接的离职人员 ID。
    /// </summary>
    public IReadOnlyList<string> DepartedPersonIds { get; set; } = [];
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
    /// <param name="existingMemberships">库中现有的人员所属部门关系。</param>
    /// <param name="clientAppCodes">已登记接入应用编码。</param>
    /// <returns>按数据行排序的问题列表。</returns>
    public static IReadOnlyList<OrganizationImportIssue> Validate(OrganizationImportBatch batch,
        IReadOnlyCollection<DepartmentEntity> existingDepartments, IReadOnlyCollection<PersonEntity> existingPeople,
        IReadOnlyCollection<PersonDepartmentEntity> existingMemberships,
        IReadOnlyCollection<string> clientAppCodes)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(existingDepartments);
        ArgumentNullException.ThrowIfNull(existingPeople);
        ArgumentNullException.ThrowIfNull(existingMemberships);
        ArgumentNullException.ThrowIfNull(clientAppCodes);
        var issues = new List<OrganizationImportIssue>();
        if (batch.Departments is null || batch.People is null || batch.Memberships is null ||
            batch.PersonBindings is null || batch.DepartmentBindings is null)
        {
            Add(issues, "batch", "组织部门、人员和所属部门列表不能为空。");
            return issues;
        }

        if (batch.Departments.Any(item => item is null) || batch.People.Any(item => item is null) ||
            batch.Memberships.Any(item => item is null) || batch.PersonBindings.Any(item => item is null) ||
            batch.DepartmentBindings.Any(item => item is null))
        {
            Add(issues, "batch", "组织列表项不能为空。");
            return issues;
        }

        var allDepartments = existingDepartments.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var allPeople = existingPeople.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var importedDepartmentIds = new HashSet<string>(StringComparer.Ordinal);
        var importedPersonIds = new HashSet<string>(StringComparer.Ordinal);
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
        var importedDepartmentCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < batch.People.Count; index++)
        {
            var person = batch.People[index];
            var path = $"people[{index}]";
            if (!string.IsNullOrWhiteSpace(person.Id) && !importedPersonIds.Add(person.Id))
            {
                Add(issues, path + ".id", "人员 ID 在导入批次内重复。");
            }

            if (string.IsNullOrWhiteSpace(person.Id) || string.IsNullOrWhiteSpace(person.EmployeeNumber) ||
                string.IsNullOrWhiteSpace(person.Name))
            {
                Add(issues, path, "人员 ID、工号和姓名不能为空。");
            }

            CheckLength(issues, path + ".id", person.Id, 64);
            CheckLength(issues, path + ".employeeNumber", person.EmployeeNumber, 128);
            CheckLength(issues, path + ".name", person.Name, 256);
            CheckLength(issues, path + ".phone", person.Phone, 64);
            CheckLength(issues, path + ".email", person.Email, 256);
            CheckLength(issues, path + ".managerId", person.ManagerId, 64);
            CheckLength(issues, path + ".position", person.Position, 128);
            CheckLength(issues, path + ".grade", person.Grade, 128);

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
            if (!string.IsNullOrWhiteSpace(department.Id) && !importedDepartmentIds.Add(department.Id))
            {
                Add(issues, path + ".id", "部门 ID 在导入批次内重复。");
            }

            if (string.IsNullOrWhiteSpace(department.Id) || string.IsNullOrWhiteSpace(department.Code) ||
                string.IsNullOrWhiteSpace(department.Name))
            {
                Add(issues, path, "部门 ID、编码和名称不能为空。");
            }

            CheckLength(issues, path + ".id", department.Id, 64);
            CheckLength(issues, path + ".code", department.Code, 128);
            CheckLength(issues, path + ".name", department.Name, 256);
            CheckLength(issues, path + ".parentId", department.ParentId, 64);
            CheckLength(issues, path + ".headPersonId", department.HeadPersonId, 64);

            if (!string.IsNullOrWhiteSpace(department.Code))
            {
                if (importedDepartmentCodes.TryGetValue(department.Code, out var previous))
                {
                    Add(issues, path + ".code", $"部门编码与第 {previous + 1} 行重复。");
                }
                else
                {
                    importedDepartmentCodes[department.Code] = index;
                }

                if (existingDepartments.Any(item => item.Code.Equals(department.Code,
                        StringComparison.OrdinalIgnoreCase) && item.Id != department.Id))
                {
                    Add(issues, path + ".code", "部门编码已被其他部门使用。");
                }
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

        var personBindingKeys = new HashSet<(string, string)>();
        for (var index = 0; index < batch.PersonBindings.Count; index++)
        {
            var binding = batch.PersonBindings[index];
            var path = $"personBindings[{index}]";
            if (string.IsNullOrWhiteSpace(binding.ClientAppCode) ||
                string.IsNullOrWhiteSpace(binding.ExternalUserId) || string.IsNullOrWhiteSpace(binding.PersonId))
            {
                Add(issues, path, "外部人员绑定的应用编码、外部 ID 和内部人员 ID 不能为空。");
            }
            else if (!allPeople.ContainsKey(binding.PersonId))
            {
                Add(issues, path + ".personId", "绑定的内部人员不存在。");
            }
            if (!string.IsNullOrWhiteSpace(binding.ClientAppCode) &&
                !clientAppCodes.Contains(binding.ClientAppCode, StringComparer.Ordinal))
            {
                Add(issues, path + ".clientAppCode", "绑定的接入应用不存在。");
            }

            if (!personBindingKeys.Add((binding.ClientAppCode, binding.ExternalUserId)))
            {
                Add(issues, path, "同一应用的外部人员 ID 在导入批次内重复。");
            }
            CheckLength(issues, path + ".clientAppCode", binding.ClientAppCode, 128);
            CheckLength(issues, path + ".externalId", binding.ExternalUserId, 128);
            CheckLength(issues, path + ".internalId", binding.PersonId, 64);
        }

        var departmentBindingKeys = new HashSet<(string, string)>();
        for (var index = 0; index < batch.DepartmentBindings.Count; index++)
        {
            var binding = batch.DepartmentBindings[index];
            var path = $"departmentBindings[{index}]";
            if (string.IsNullOrWhiteSpace(binding.ClientAppCode) ||
                string.IsNullOrWhiteSpace(binding.ExternalDepartmentId) ||
                string.IsNullOrWhiteSpace(binding.DepartmentId))
            {
                Add(issues, path, "外部部门绑定的应用编码、外部 ID 和内部部门 ID 不能为空。");
            }
            else if (!allDepartments.ContainsKey(binding.DepartmentId))
            {
                Add(issues, path + ".departmentId", "绑定的内部部门不存在。");
            }
            if (!string.IsNullOrWhiteSpace(binding.ClientAppCode) &&
                !clientAppCodes.Contains(binding.ClientAppCode, StringComparer.Ordinal))
            {
                Add(issues, path + ".clientAppCode", "绑定的接入应用不存在。");
            }

            if (!departmentBindingKeys.Add((binding.ClientAppCode, binding.ExternalDepartmentId)))
            {
                Add(issues, path, "同一应用的外部部门 ID 在导入批次内重复。");
            }
            CheckLength(issues, path + ".clientAppCode", binding.ClientAppCode, 128);
            CheckLength(issues, path + ".externalId", binding.ExternalDepartmentId, 128);
            CheckLength(issues, path + ".internalId", binding.DepartmentId, 64);
        }

        var allMemberships = existingMemberships.ToDictionary(item => (item.PersonId, item.DepartmentId));
        var importedMembershipKeys = new HashSet<(string PersonId, string DepartmentId)>();
        for (var index = 0; index < batch.Memberships.Count; index++)
        {
            var membership = batch.Memberships[index];
            var key = (membership.PersonId, membership.DepartmentId);
            if (!importedMembershipKeys.Add(key))
            {
                Add(issues, $"memberships[{index}]", "同一人员和部门关系在导入批次内重复。");
            }

            allMemberships[key] = membership;
        }

        var primaryCount = allMemberships.Values.Where(item => item.IsPrimary)
            .GroupBy(item => item.PersonId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        for (var index = 0; index < batch.Memberships.Count; index++)
        {
            var membership = batch.Memberships[index];
            if (membership.IsPrimary && primaryCount.GetValueOrDefault(membership.PersonId) > 1)
            {
                Add(issues, $"memberships[{index}].isPrimary", "同一人员只能有一个主部门。");
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

    /// <summary>
    /// 按数据库列宽检查输入，避免不同数据库在正式保存时给出内部错误。
    /// </summary>
    /// <param name="issues">问题列表。</param>
    /// <param name="field">字段路径。</param>
    /// <param name="value">输入值。</param>
    /// <param name="maximum">最大字符数。</param>
    private static void CheckLength(List<OrganizationImportIssue> issues, string field, string? value, int maximum)
    {
        if (value?.Length > maximum)
        {
            Add(issues, field, $"长度不能超过 {maximum} 个字符。");
        }
    }
}
