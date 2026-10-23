using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 纯引擎只对实际到达的业务取人节点请求事务前预取。
/// </summary>
internal static class EngineScenarioChecksBusinessApi
{
    /// <summary>
    /// 检查顺序、条件分支和无预取结果的确定性重算。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
        ConfigureBusinessNode(graph.Nodes.Single(item => item.Id == "n1"), "leader");
        ConfigureBusinessNode(graph.Nodes.Single(item => item.Id == "n2"), "leader");
        var context = Context(graph);
        var startCommand = EngineScenarioChecks.Command(EngineCommandKinds.Start, "business-start");
        var firstNeed = EngineRuntime.Execute(null, startCommand, context);
        check("业务取人缺预取时只请求实际首节点且不产出可提交事件",
            firstNeed.RequiredBusinessAssignee is { NodeId: "n1", SourceValue: "leader" } &&
            firstNeed.Events.Count == 0 && firstNeed.State.Tasks.Count == 0 &&
            firstNeed.State.ProcessedRequestIds.Count == 0);

        context.AssigneeSnapshot!.BusinessApiResults["n1"] = ["person-1"];
        var first = EngineRuntime.Execute(null, startCommand, context);
        check("补齐首节点取人结果后以相同命令建立实际任务",
            first.RequiredBusinessAssignee is null && first.State.Tasks.Single().AssigneeId == "person-1" &&
            first.Events.Any(item => item.Kind == "instanceStarted"));

        var approve = EngineScenarioChecks.Command(EngineCommandKinds.Approve, "business-approve",
            first.State.Tasks.Single().Id);
        var nextNeed = EngineRuntime.Execute(first.State, approve, context);
        check("前一任务推进到下一个同来源节点时仍按节点单独取人",
            nextNeed.RequiredBusinessAssignee is { NodeId: "n2" } && nextNeed.Events.Count == 0 &&
            nextNeed.State.Tasks.Single().Status == EngineTaskStatuses.Pending &&
            first.State.Tasks.Single().Status == EngineTaskStatuses.Pending);

        context.AssigneeSnapshot.BusinessApiResults["n2"] = ["person-2"];
        var second = EngineRuntime.Execute(first.State, approve, context);
        check("第二节点补取人后稳定重算前一人工命令",
            second.RequiredBusinessAssignee is null && second.State.Tasks.Count == 2 &&
            second.State.Tasks[^1].AssigneeId == "person-2" &&
            second.State.Tasks[0].Status == EngineTaskStatuses.Approved);

        var exclusive = EngineScenarioChecks.GatewayGraph(FlowNodeTypes.ExclusiveGateway);
        ConfigureBusinessNode(exclusive.Nodes.Single(item => item.Id == "a"), "branch-a");
        ConfigureBusinessNode(exclusive.Nodes.Single(item => item.Id == "b"), "branch-b");
        var branchContext = Context(exclusive);
        branchContext.Conditions = new Dictionary<string, bool> { ["split-a"] = true };
        var branchNeed = EngineRuntime.Execute(null,
            EngineScenarioChecks.Command(EngineCommandKinds.Start, "business-branch"), branchContext);
        check("排他网关只为命中分支取人且不触发未走分支",
            branchNeed.RequiredBusinessAssignee?.NodeId == "a" && branchNeed.Events.Count == 0);

        branchContext.AssigneeSnapshot!.BusinessApiResults["a"] = ["person-1"];
        var selected = EngineRuntime.Execute(null,
            EngineScenarioChecks.Command(EngineCommandKinds.Start, "business-branch"), branchContext);
        check("命中分支补齐后不要求未走分支的业务接口",
            selected.RequiredBusinessAssignee is null && selected.State.Tasks.Single().NodeId == "a");
    }

    /// <summary>
    /// 给人工节点配置业务系统接口来源。
    /// </summary>
    /// <param name="node">发布图人工节点。</param>
    /// <param name="sourceValue">业务来源编码。</param>
    private static void ConfigureBusinessNode(FlowExecutionNode node, string sourceValue) =>
        node.Assignee = new FlowAssignee { Source = FlowAssigneeSources.BusinessApi, Value = sourceValue };

    /// <summary>
    /// 提供有两位在职处理人的可信组织快照。
    /// </summary>
    /// <param name="graph">执行图。</param>
    /// <returns>可重复使用的纯引擎输入。</returns>
    private static EngineContext Context(FlowExecutionGraph graph) => new()
    {
        Graph = graph,
        OccurredAt = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero),
        AssigneeSnapshot = new AssigneeSnapshot
        {
            InitiatorId = "initiator",
            People =
            [
                new AssigneePerson { Id = "initiator" },
                new AssigneePerson { Id = "person-1" },
                new AssigneePerson { Id = "person-2" },
            ],
        },
    };
}
