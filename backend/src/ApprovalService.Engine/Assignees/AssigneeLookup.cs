namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 审批人来源共用的确定性组织查找函数。
/// </summary>
internal static class AssigneeLookup
{
    /// <summary>
    /// 沿发起部门向上找到指定级数的部门。
    /// </summary>
    /// <param name="snapshot">组织快照。</param>
    /// <param name="departmentId">起始部门。</param>
    /// <param name="level">向上级数，零为本部门。</param>
    /// <returns>目标部门，找不到时为空。</returns>
    public static AssigneeDepartment? DepartmentAt(AssigneeSnapshot snapshot, string departmentId, int level)
    {
        var department = snapshot.Departments.SingleOrDefault(item => item.Id == departmentId);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < level && department is not null; index++)
        {
            if (!visited.Add(department.Id))
            {
                return null;
            }

            department = snapshot.Departments.SingleOrDefault(item => item.Id == department.ParentId);
        }

        return department;
    }

    /// <summary>
    /// 沿人员直属上级关系找第 N 级。
    /// </summary>
    /// <param name="snapshot">组织快照。</param>
    /// <param name="personId">起点人员。</param>
    /// <param name="level">级数，从一开始。</param>
    /// <returns>目标人员，找不到时为空。</returns>
    public static AssigneePerson? ManagerAt(AssigneeSnapshot snapshot, string personId, int level)
    {
        var person = snapshot.People.SingleOrDefault(item => item.Id == personId);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < level && person is not null; index++)
        {
            if (!visited.Add(person.Id))
            {
                return null;
            }

            person = snapshot.People.SingleOrDefault(item => item.Id == person.ManagerId);
        }

        return person;
    }

    /// <summary>
    /// 清除空项和重复人员，保持首次出现顺序。
    /// </summary>
    /// <param name="ids">原始人员 ID。</param>
    /// <returns>稳定的唯一人员列表。</returns>
    public static IReadOnlyList<string> Distinct(IEnumerable<string?> ids) => ids
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id!)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
