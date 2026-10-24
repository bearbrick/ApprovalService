using ApprovalService.Client;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 设计书中的虚构演示组织：可重复生成相同内部 ID 和接入应用绑定。
/// </summary>
public static class DemoOrganizationSeed
{
    /// <summary>
    /// 构造七个部门、十九名人员及李娜的双部门关系。
    /// </summary>
    /// <returns>供一键准备环境使用的组织和绑定计划。</returns>
    public static DemoOrganizationPlan Build()
    {
        var changes = new OrganizationChangesRequest();
        var bindings = new List<OrganizationBinding>();
        AddDepartment(changes, bindings, "office", "总经办", null, "zhou");
        AddDepartment(changes, bindings, "production", "生产部", "office", "zhang");
        AddDepartment(changes, bindings, "production-one", "生产一部", "production", "li-na");
        AddDepartment(changes, bindings, "research", "研发部", "office", "sun");
        AddDepartment(changes, bindings, "purchase", "采购部", "office", "wu");
        AddDepartment(changes, bindings, "finance", "财务部", "office", "he");
        AddDepartment(changes, bindings, "administration", "行政部", "office", "huang");

        AddPerson(changes, bindings, "zhou", "周总", null, "office", "GENERAL_MANAGER", 9);
        AddPerson(changes, bindings, "liu-vice", "刘副总", "zhou", "office", "VICE_GENERAL_MANAGER", 8);
        AddPerson(changes, bindings, "chen-vice", "陈副总", "zhou", "office", "VICE_GENERAL_MANAGER", 8);
        AddPerson(changes, bindings, "zhang", "张明", "liu-vice", "production", "DEPARTMENT_HEAD", 6);
        AddPerson(changes, bindings, "ma", "马丽", "zhang", "production", "PLANNER", 4);
        AddPerson(changes, bindings, "li-na", "李娜", "zhang", "production-one", "PROJECT_MANAGER", 5);
        AddPerson(changes, bindings, "wang", "王强", "li-na", "production-one", "EMPLOYEE", 2);
        AddPerson(changes, bindings, "zhao-gang", "赵刚", "li-na", "production-one", "EMPLOYEE", 2);
        AddPerson(changes, bindings, "sun", "孙磊", "liu-vice", "research", "DEPARTMENT_HEAD", 6);
        AddPerson(changes, bindings, "wu", "吴芳", "chen-vice", "purchase", "PURCHASE_MANAGER", 6);
        AddPerson(changes, bindings, "zheng", "郑涛", "wu", "purchase", "BUYER", 3);
        AddPerson(changes, bindings, "feng", "冯伟", "chen-vice", "finance", "FINANCE_DIRECTOR", 7);
        AddPerson(changes, bindings, "he", "何静", "feng", "finance", "FINANCE_MANAGER", 6);
        AddPerson(changes, bindings, "zhao-min", "赵敏", "he", "finance", "ACCOUNTANT", 4);
        AddPerson(changes, bindings, "qian", "钱丽", "he", "finance", "ACCOUNTANT", 4);
        AddPerson(changes, bindings, "liu-yang", "刘洋", "he", "finance", "CASHIER", 3);
        AddPerson(changes, bindings, "huang", "黄蕾", "liu-vice", "administration", "DEPARTMENT_HEAD", 6);
        AddPerson(changes, bindings, "lin", "林峰", "huang", "administration", "ASSET_MANAGER", 4);
        AddPerson(changes, bindings, "gao", "高远", "huang", "administration", "LEGAL", 5);

        // 李娜以研发部发起时，服务端能验证她确实属于所选部门。
        changes.Memberships.Add(new OrganizationMembership
        {
            PersonId = PersonId("li-na"), DepartmentId = DepartmentId("research"), IsPrimary = false,
        });
        return new DemoOrganizationPlan(changes, bindings);
    }

    /// <summary>
    /// 调 SDK 推送组织变化并建立本接入应用的人员与部门绑定。
    /// </summary>
    /// <param name="client">持有演示接入应用 API Key 的 SDK 客户端。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部变化与绑定完成。</returns>
    public static async Task ApplyAsync(ApprovalServiceClient client, CancellationToken cancellationToken = default)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        var plan = Build();
        var changes = await client.PushOrganizationChangesAsync(plan.Changes, cancellationToken).ConfigureAwait(false);
        if (!changes.Applied)
        {
            var first = changes.Issues.FirstOrDefault();
            throw new InvalidOperationException(first == null ? "演示组织推送未成功。" :
                "演示组织推送未成功：" + first.Message);
        }

        foreach (var binding in plan.Bindings)
        {
            await client.BindOrganizationIdAsync(binding, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 为内部人员生成固定 ID。
    /// </summary>
    /// <param name="code">演示人员短码。</param>
    /// <returns>审批服务内部人员 ID。</returns>
    private static string PersonId(string code) => "demo-person-" + code;

    /// <summary>
    /// 为内部部门生成固定 ID。
    /// </summary>
    /// <param name="code">演示部门短码。</param>
    /// <returns>审批服务内部部门 ID。</returns>
    private static string DepartmentId(string code) => "demo-dept-" + code;

    /// <summary>
    /// 登记一个演示部门和当前应用的部门绑定。
    /// </summary>
    /// <param name="changes">组织批次。</param>
    /// <param name="bindings">应用绑定集合。</param>
    /// <param name="code">部门短码。</param>
    /// <param name="name">部门名称。</param>
    /// <param name="parent">上级短码。</param>
    /// <param name="head">负责人短码。</param>
    private static void AddDepartment(OrganizationChangesRequest changes, List<OrganizationBinding> bindings,
        string code, string name, string? parent, string head)
    {
        var id = DepartmentId(code);
        changes.Departments.Add(new OrganizationDepartment
        {
            Id = id, Code = "DEMO-" + code.ToUpperInvariant(), Name = name,
            ParentId = parent == null ? null : DepartmentId(parent), HeadPersonId = PersonId(head),
        });
        bindings.Add(new OrganizationBinding { Kind = "department", ExternalId = code, InternalId = id });
    }

    /// <summary>
    /// 登记人员、主部门关系与按工号匹配的应用绑定。
    /// </summary>
    /// <param name="changes">组织批次。</param>
    /// <param name="bindings">应用绑定集合。</param>
    /// <param name="code">业务用户短码。</param>
    /// <param name="name">显示姓名。</param>
    /// <param name="manager">直属上级短码。</param>
    /// <param name="department">主部门短码。</param>
    /// <param name="position">岗位编码。</param>
    /// <param name="rank">演示职级序数。</param>
    private static void AddPerson(OrganizationChangesRequest changes, List<OrganizationBinding> bindings,
        string code, string name, string? manager, string department, string position, int rank)
    {
        var id = PersonId(code);
        var employeeNumber = "DEMO-" + code.ToUpperInvariant();
        changes.People.Add(new OrganizationPerson
        {
            Id = id, EmployeeNumber = employeeNumber, Name = name,
            ManagerId = manager == null ? null : PersonId(manager),
            Position = position, Grade = "G" + rank, GradeRank = rank, IsActive = true,
        });
        changes.Memberships.Add(new OrganizationMembership
        {
            PersonId = id, DepartmentId = DepartmentId(department), IsPrimary = true,
        });
        bindings.Add(new OrganizationBinding { Kind = "person", ExternalId = code, EmployeeNumber = employeeNumber });
    }
}

/// <summary>
/// 可预览的一键演示组织准备计划。
/// </summary>
public sealed class DemoOrganizationPlan
{
    /// <summary>
    /// 创建组织准备计划。
    /// </summary>
    /// <param name="changes">部门、人员与归属。</param>
    /// <param name="bindings">当前应用的外部 ID 绑定。</param>
    internal DemoOrganizationPlan(OrganizationChangesRequest changes, IReadOnlyList<OrganizationBinding> bindings)
    {
        Changes = changes;
        Bindings = bindings;
    }

    /// <summary>
    /// 将要推送的组织资料。
    /// </summary>
    public OrganizationChangesRequest Changes { get; }

    /// <summary>
    /// 将要建立的人员及部门绑定。
    /// </summary>
    public IReadOnlyList<OrganizationBinding> Bindings { get; }
}
