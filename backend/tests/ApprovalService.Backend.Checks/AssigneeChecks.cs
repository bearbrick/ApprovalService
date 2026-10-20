using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Assignees;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 十四类找人场景；岗位与职级作为两个来源代码检查。
/// </summary>
internal static class AssigneeChecks
{
    /// <summary>
    /// 检查组织、数据、选择与接口来源的纯解析结果。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var snapshot = Snapshot();
        check("指定成员保持配置顺序并去重", Find("member", snapshot, value: "m1,m2,m1").SequenceEqual(["m1", "m2"]));
        check("角色可限定发起部门而非人员的其他部门", Find("role", snapshot, value: "clerk", scoped: true)
            .SequenceEqual(["clerk2"]));
        check("岗位按编码匹配", Find("position", snapshot, value: "buyer").SequenceEqual(["clerk1"]));
        check("职级按最低级别向上匹配", Find("grade", snapshot, value: "G2").SequenceEqual(["m1", "m2"]));
        check("直属上级支持第 N 级", Find("manager", snapshot, level: 2).SequenceEqual(["m2"]));
        check("连续上级可遇职级停止", Find("managerChain", snapshot, level: 3, stopAtGrade: "G2")
            .SequenceEqual(["m1"]));
        check("部门负责人按发起部门向上第 N 级", Find("departmentHead", snapshot, level: 2)
            .SequenceEqual(["h1"]));
        check("业务数据部门找负责人", Find("dataDepartmentHead", snapshot, fieldKey: "chargedDepartment")
            .SequenceEqual(["h3"]));
        check("分管领导按发起部门找", Find("divisionLeader", snapshot).SequenceEqual(["vp"]));
        check("分管领导可按业务范围找", Find("divisionLeader", snapshot, value: "production")
            .SequenceEqual(["vp"]));
        check("业务数据人员字段找人", Find("dataPerson", snapshot, fieldKey: "owner").SequenceEqual(["clerk1"]));
        check("审批矩阵使用调用方传入的命中快照", Find("matrix", snapshot, value: "amountMatrix")
            .SequenceEqual(["m1", "vp"]));
        check("业务系统接口只消费预取结果", Find("businessApi", snapshot, value: "projectManager")
            .SequenceEqual(["m2"]));
        check("上一步指定读取当前节点选择", Find("previousSelection", snapshot).SequenceEqual(["m1"]));
        check("发起人自选读取当前节点选择", Find("initiatorSelection", snapshot).SequenceEqual(["vp"]));
        check("发起人本人可作确认处理人", Find("initiator", snapshot).SequenceEqual(["initiator"]));

        snapshot.InitiatingDepartmentId = "d1";
        check("多部门发起人切换发起部门后重新找人", Find("role", snapshot, value: "clerk", scoped: true)
            .SequenceEqual(["clerk1"]));

        var extensionSnapshot = Snapshot();
        var extensionRequest = new AssigneeRequest
        {
            NodeId = "approval",
            Configuration = new FlowAssignee { Source = "custom" },
        };
        var extensionResult = AssigneeResolverRegistry.Resolve(extensionRequest, extensionSnapshot,
            [new MutatingResolver()]);
        check("自定义找人来源不能修改原组织和配置输入", extensionResult.AssigneeIds.SequenceEqual(["m1"]) &&
            extensionSnapshot.InitiatingDepartmentId == "d2" && extensionRequest.Configuration.Source == "custom");
    }

    /// <summary>
    /// 使用一个内置来源找人。
    /// </summary>
    /// <param name="source">来源编码。</param>
    /// <param name="snapshot">输入快照。</param>
    /// <param name="value">来源配置编码。</param>
    /// <param name="fieldKey">数据字段。</param>
    /// <param name="level">层级。</param>
    /// <param name="scoped">是否限定发起部门。</param>
    /// <param name="stopAtGrade">连续上级截止职级。</param>
    /// <returns>解析出的人员 ID。</returns>
    private static IReadOnlyList<string> Find(string source, AssigneeSnapshot snapshot, string? value = null,
        string? fieldKey = null, int? level = null, bool scoped = false, string? stopAtGrade = null) =>
        AssigneeResolverRegistry.Resolve(new AssigneeRequest
        {
            NodeId = "approval",
            Configuration = new FlowAssignee { Source = source, Value = value, FieldKey = fieldKey, Level = level },
            LimitToInitiatingDepartment = scoped,
            StopAtGrade = stopAtGrade,
        }, snapshot).AssigneeIds;

    /// <summary>
    /// 创建覆盖多部门、上级、角色、矩阵和业务接口的组织快照。
    /// </summary>
    /// <returns>样例快照。</returns>
    private static AssigneeSnapshot Snapshot() => new()
    {
        InitiatorId = "initiator",
        InitiatingDepartmentId = "d2",
        AdministratorId = "admin",
        People =
        [
            new() { Id = "initiator", DepartmentIds = ["d1", "d2"], ManagerId = "m1" },
            new() { Id = "m1", DepartmentIds = ["d2"], ManagerId = "m2", Grade = "G2", GradeRank = 2 },
            new() { Id = "m2", DepartmentIds = ["d1"], Grade = "G3", GradeRank = 3 },
            new() { Id = "clerk1", DepartmentIds = ["d1"], Roles = ["clerk"], Position = "buyer" },
            new() { Id = "clerk2", DepartmentIds = ["d2"], Roles = ["clerk"] },
            new() { Id = "h1", DepartmentIds = ["d1"] },
            new() { Id = "h2", DepartmentIds = ["d2"] },
            new() { Id = "h3", DepartmentIds = ["d3"] },
            new() { Id = "vp", DepartmentIds = ["d1"] },
        ],
        Departments =
        [
            new() { Id = "d1", HeadId = "h1" },
            new() { Id = "d2", ParentId = "d1", HeadId = "h2", DivisionLeaderIds = ["vp"],
                DivisionLeadersByArea = new Dictionary<string, List<string>> { ["production"] = ["vp"] } },
            new() { Id = "d3", HeadId = "h3" },
        ],
        DataValues = new Dictionary<string, string>
        {
            ["owner"] = "clerk1",
            ["chargedDepartment"] = "d3",
        },
        MatrixMatches = [new() { MatrixCode = "amountMatrix", AssigneeIds = ["m1", "vp"] }],
        BusinessApiResults = new Dictionary<string, List<string>> { ["projectManager"] = ["m2"] },
        PreviousSelections = new Dictionary<string, List<string>> { ["approval"] = ["m1"] },
        InitiatorSelections = new Dictionary<string, List<string>> { ["approval"] = ["vp"] },
    };

    /// <summary>
    /// 故意修改输入以检验扩展隔离。
    /// </summary>
    private sealed class MutatingResolver : IAssigneeResolver
    {
        /// <inheritdoc />
        public string Source => "custom";

        /// <inheritdoc />
        public IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot)
        {
            request.Configuration.Source = "changed";
            snapshot.InitiatingDepartmentId = "changed";
            return ["m1"];
        }
    }
}
