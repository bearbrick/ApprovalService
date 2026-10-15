using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 引擎运行时的剧本场景；直接使用发布执行图，不经过数据库或网络。
/// </summary>
internal static class EngineScenarioChecks
{
    /// <summary>
    /// 运行各节点、网关、状态、幂等和上限场景。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    public static void Run(Action<string, bool> check)
    {
        CheckHumanNodes(check);
        CheckGateways(check);
        CheckExternalNodes(check);
        CheckLimits(check);
    }

    /// <summary>
    /// 检查开始、结束、审批、办理、抄送以及命令幂等。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckHumanNodes(Action<string, bool> check)
    {
        var graph = Chain(FlowNodeTypes.Approval, FlowNodeTypes.Handling, FlowNodeTypes.Cc);
        var context = Context(graph);
        var startCommand = Command(EngineCommandKinds.Start, "s");
        var first = EngineRuntime.Execute(null, startCommand, context);
        check("开始节点生成单人审批任务", first.State.Status == EngineInstanceStatuses.Running &&
            first.State.Tasks.Count == 1 && first.State.Tasks[0].NodeId == "n1" &&
            first.Events.Any(item => item.Kind == "instanceStarted"));

        var replay = EngineRuntime.Execute(first.State, startCommand, context);
        check("同一请求 ID 幂等且不修改输入状态", replay.IsReplay && replay.Events.Count == 0 &&
            replay.State.Tasks.Count == 1 && !ReferenceEquals(replay.State, first.State));
        var otherInstance = EngineRuntime.Execute(null,
            new EngineCommand { Kind = EngineCommandKinds.Start, RequestId = "s", InstanceId = "other" }, context);
        check("不同审批单复用请求 ID 不产生相同事件 ID", first.Events[0].Id != otherInstance.Events[0].Id);

        var second = EngineRuntime.Execute(first.State, Command(EngineCommandKinds.Approve, "a", first.State.Tasks[0].Id), context);
        check("审批同意后转到办理且原状态不变", second.State.Tasks.Count == 2 &&
            second.State.Tasks[^1].NodeId == "n2" && first.State.Tasks[0].Status == EngineTaskStatuses.Pending);

        var third = EngineRuntime.Execute(second.State, Command(EngineCommandKinds.Handle, "h", second.State.Tasks[^1].Id), context);
        check("办理完成后无已阅要求的抄送自动通过", third.State.Status == EngineInstanceStatuses.Approved &&
            third.Events.Any(item => item.Kind == "ccDelivered") && third.State.Tokens.Count == 0);

        var cancel = EngineRuntime.Execute(third.State, Command(EngineCommandKinds.Cancel, "c"), context);
        check("已通过审批单可撤销且撤销重复命令幂等", cancel.State.Status == EngineInstanceStatuses.Cancelled &&
            EngineRuntime.Execute(cancel.State, Command(EngineCommandKinds.Cancel, "c"), context).IsReplay);

        var ccGraph = Chain(FlowNodeTypes.Cc);
        ccGraph.Nodes.Single(item => item.Id == "n1").Approval = new FlowApprovalSettings { ReadReceiptRequired = true };
        var cc = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "cc-start"), Context(ccGraph));
        var acknowledged = EngineRuntime.Execute(cc.State,
            Command(EngineCommandKinds.Acknowledge, "cc-read", cc.State.Tasks[0].Id), Context(ccGraph));
        check("抄送要求已阅时生成任务并在确认后结束", acknowledged.State.Status == EngineInstanceStatuses.Approved);

        var rejection = EngineRuntime.Execute(first.State, Command(EngineCommandKinds.Reject, "r", first.State.Tasks[0].Id), context);
        check("审批拒绝结束单据并取消剩余令牌", rejection.State.Status == EngineInstanceStatuses.Rejected &&
            rejection.State.Tasks[0].Status == EngineTaskStatuses.Rejected && rejection.State.Tokens.Count == 0);

        var withdrawn = EngineRuntime.Execute(first.State, Command(EngineCommandKinds.Withdraw, "w"), context);
        var terminated = EngineRuntime.Execute(first.State, Command(EngineCommandKinds.Terminate, "t"), context);
        check("撤回和终止都会取消未处理任务", withdrawn.State.Status == EngineInstanceStatuses.Withdrawn &&
            terminated.State.Status == EngineInstanceStatuses.Terminated &&
            withdrawn.State.Tasks[0].Status == EngineTaskStatuses.Cancelled &&
            terminated.State.Tasks[0].Status == EngineTaskStatuses.Cancelled);

        check("已处理任务不能再次操作", ThrowsConflict(() => EngineRuntime.Execute(second.State,
            Command(EngineCommandKinds.Approve, "again", second.State.Tasks[0].Id), context)));
    }

    /// <summary>
    /// 检查排他、包容、并行的分支选择和同次分叉的汇合。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckGateways(Action<string, bool> check)
    {
        var exclusive = GatewayGraph(FlowNodeTypes.ExclusiveGateway);
        var exContext = Context(exclusive, true, true);
        var ex = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "ex"), exContext);
        check("排他网关按优先级只走第一条命中分支", ex.State.Tasks.Count == 1 &&
            ex.State.Tasks[0].NodeId == "a");
        var exDone = EngineRuntime.Execute(ex.State, Command(EngineCommandKinds.Approve, "ex-approve", ex.State.Tasks[0].Id), exContext);
        check("排他汇合不等待未选择的分支", exDone.State.Status == EngineInstanceStatuses.Approved);

        var fallback = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "ex-default"), Context(exclusive));
        check("条件全部不命中时排他网关走兜底", fallback.State.Tasks.Single().NodeId == "b");
        check("未提供条件求值结果时不能静默走兜底", ThrowsValidation(() => EngineRuntime.Execute(null,
            Command(EngineCommandKinds.Start, "ex-missing"), new EngineContext { Graph = exclusive })));

        var inclusive = GatewayGraph(FlowNodeTypes.InclusiveGateway);
        var one = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "in-one"), Context(inclusive, true));
        var oneDone = EngineRuntime.Execute(one.State,
            Command(EngineCommandKinds.Approve, "in-one-ok", one.State.Tasks[0].Id), Context(inclusive, true));
        check("包容网关只等待实际激活的分支", one.State.Tokens.Count == 1 &&
            oneDone.State.Status == EngineInstanceStatuses.Approved);

        var bothContext = Context(inclusive, true, true);
        var both = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "in-both"), bothContext);
        var afterA = EngineRuntime.Execute(both.State,
            Command(EngineCommandKinds.Approve, "in-a", both.State.Tasks[0].Id), bothContext);
        check("包容网关在另一已激活分支未完成前等待", afterA.State.Status == EngineInstanceStatuses.Running &&
            afterA.State.Tokens.Count == 2 && afterA.State.Tokens.Any(item => item.WaitingFor == "join"));
        var afterB = EngineRuntime.Execute(afterA.State,
            Command(EngineCommandKinds.Approve, "in-b", afterA.State.Tasks[1].Id), bothContext);
        check("包容网关在实际分支齐全后恰好推进一次", afterB.State.Status == EngineInstanceStatuses.Approved &&
            afterB.Events.Count(item => item.Kind == "gatewayJoined") == 1);

        var parallel = GatewayGraph(FlowNodeTypes.ParallelGateway);
        var par = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "par"), Context(parallel));
        var parOne = EngineRuntime.Execute(par.State,
            Command(EngineCommandKinds.Approve, "par-a", par.State.Tasks[0].Id), Context(parallel));
        var parDone = EngineRuntime.Execute(parOne.State,
            Command(EngineCommandKinds.Approve, "par-b", parOne.State.Tasks[1].Id), Context(parallel));
        check("并行网关等待全部分支且只完成一次", par.State.Tasks.Count == 2 &&
            parOne.State.Status == EngineInstanceStatuses.Running && parDone.State.Status == EngineInstanceStatuses.Approved &&
            parDone.Events.Count(item => item.Kind == "instanceApproved") == 1);

        var nested = NestedGatewayGraph();
        var nestedContext = Context(nested, true);
        nestedContext.Conditions = new Dictionary<string, bool> { ["inner-a"] = true, ["inner-b"] = true };
        var nestedState = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "nested"), nestedContext).State;
        check("嵌套分叉产生各自独立的汇合栈", nestedState.Tokens.Count == 3 && nestedState.Tasks.Count == 3);
        for (var index = 0; index < 3; index++)
        {
            var task = nestedState.Tasks.First(item => item.Status == EngineTaskStatuses.Pending);
            nestedState = EngineRuntime.Execute(nestedState,
                Command(EngineCommandKinds.Approve, "nested-" + index, task.Id), nestedContext).State;
        }

        check("内外两层网关均等待正确分支后完成", nestedState.Status == EngineInstanceStatuses.Approved &&
            nestedState.Tokens.Count == 0);
    }

    /// <summary>
    /// 检查子流程和自动动作只产生待发事件，由结果命令恢复。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckExternalNodes(Action<string, bool> check)
    {
        var action = Chain(FlowNodeTypes.Automatic);
        action.Nodes.Single(item => item.Id == "n1").Automation = new FlowAutomationSettings { ActionCode = "make-order" };
        var pending = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "action"), Context(action));
        check("自动节点产生待执行动作事件而不直接执行", pending.State.Tokens.Single().WaitingFor == "action" &&
            pending.Events.Any(item => item.Kind == "actionPending" && item.TargetCode == "make-order"));
        var success = EngineRuntime.Execute(pending.State,
            Command(EngineCommandKinds.ActionSucceeded, "action-ok", tokenId: pending.State.Tokens.Single().Id), Context(action));
        var failure = EngineRuntime.Execute(pending.State,
            Command(EngineCommandKinds.ActionFailed, "action-fail", tokenId: pending.State.Tokens.Single().Id), Context(action));
        check("动作结果命令分别推进或标记异常", success.State.Status == EngineInstanceStatuses.Approved &&
            failure.State.Status == EngineInstanceStatuses.Faulted);

        var child = Chain(FlowNodeTypes.Subprocess);
        child.Nodes.Single(item => item.Id == "n1").Subprocess = new FlowSubprocessSettings { FlowCode = "CHILD", WaitForCompletion = true };
        var waiting = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "child"), Context(child));
        check("等待型子流程发出创建事件并暂停父流程", waiting.State.Tokens.Single().WaitingFor == "subprocess" &&
            waiting.Events.Any(item => item.Kind == "subprocessRequested" && item.TargetCode == "CHILD"));
        var resumed = EngineRuntime.Execute(waiting.State,
            Command(EngineCommandKinds.SubprocessCompleted, "child-ok", tokenId: waiting.State.Tokens.Single().Id), Context(child));
        check("子流程完成命令恢复父流程", resumed.State.Status == EngineInstanceStatuses.Approved);
        child.Nodes.Single(item => item.Id == "n1").Subprocess!.WaitForCompletion = false;
        var detached = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "detached"), Context(child));
        check("不等待型子流程发出创建事件后继续推进", detached.State.Status == EngineInstanceStatuses.Approved &&
            detached.Events.Any(item => item.Kind == "subprocessRequested"));
    }

    /// <summary>
    /// 检查子流程深度与回路步数上限。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    private static void CheckLimits(Action<string, bool> check)
    {
        var child = Chain(FlowNodeTypes.Subprocess);
        child.Nodes.Single(item => item.Id == "n1").Subprocess = new FlowSubprocessSettings { FlowCode = "CHILD" };
        var depthContext = Context(child);
        depthContext.SubprocessDepth = EngineLimits.MaxSubprocessDepth;
        check("子流程达到最大嵌套层级时明确报错", ThrowsValidation(() =>
            EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "too-deep"), depthContext)));

        var loop = LoopGraph();
        var once = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "loop-once"), Context(loop, true));
        var round = EngineRuntime.Execute(once.State,
            Command(EngineCommandKinds.Approve, "loop-first", once.State.Tasks[0].Id), Context(loop, true));
        var exited = EngineRuntime.Execute(round.State,
            Command(EngineCommandKinds.Approve, "loop-last", round.State.Tasks[^1].Id), Context(loop));
        check("返工回路可以重审一次后走兜底出口", round.State.Tasks.Count == 2 &&
            exited.State.Status == EngineInstanceStatuses.Approved);

        var state = EngineRuntime.Execute(null, Command(EngineCommandKinds.Start, "loop-start"), Context(loop, true)).State;
        for (var index = 0; index < EngineLimits.MaxStepsPerInstance && state.Status == EngineInstanceStatuses.Running; index++)
        {
            state = EngineRuntime.Execute(state,
                Command(EngineCommandKinds.Approve, "loop-" + index, state.Tasks.Last().Id), Context(loop, true)).State;
        }

        check("自由画布回路超过累计步数后停止并标记异常", state.Status == EngineInstanceStatuses.Faulted &&
            state.StepCount == EngineLimits.MaxStepsPerInstance + 1);
    }

    /// <summary>
    /// 建立开始、指定节点和结束的顺序图。
    /// </summary>
    /// <param name="types">中间节点种类。</param>
    /// <returns>可执行图。</returns>
    internal static FlowExecutionGraph Chain(params string[] types)
    {
        var nodes = new List<FlowExecutionNode> { new() { Id = "start", Type = FlowNodeTypes.Start } };
        for (var index = 0; index < types.Length; index++)
        {
            nodes.Add(new FlowExecutionNode { Id = "n" + (index + 1), Type = types[index] });
        }

        nodes.Add(new FlowExecutionNode { Id = "end", Type = FlowNodeTypes.End });
        return new FlowExecutionGraph
        {
            Nodes = nodes,
            Edges = Enumerable.Range(0, nodes.Count - 1).Select(index =>
                new FlowExecutionEdge { Id = "e" + index, SourceId = nodes[index].Id, TargetId = nodes[index + 1].Id }).ToList(),
        };
    }

    /// <summary>
    /// 建立带单人处理人和条件结果的固定输入。
    /// </summary>
    /// <param name="graph">执行图。</param>
    /// <param name="first">第一条条件分支是否成立。</param>
    /// <param name="second">第二条条件分支是否成立。</param>
    /// <returns>引擎上下文。</returns>
    internal static EngineContext Context(FlowExecutionGraph graph, bool first = false, bool second = false) => new()
    {
        Graph = graph,
        OccurredAt = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero),
        Assignees = graph.Nodes.Where(item => item.Type is FlowNodeTypes.Approval or FlowNodeTypes.Handling or FlowNodeTypes.Cc)
            .ToDictionary(item => item.Id, item => "user-" + item.Id, StringComparer.Ordinal),
        Conditions = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["split-a"] = first,
            ["split-b"] = second,
            ["loop-back"] = first,
        },
    };

    /// <summary>
    /// 建立带确定性请求 ID 的命令。
    /// </summary>
    /// <param name="kind">命令种类。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="tokenId">等待令牌 ID。</param>
    /// <returns>命令。</returns>
    internal static EngineCommand Command(string kind, string requestId, string? taskId = null, string? tokenId = null) => new()
    {
        Kind = kind,
        RequestId = requestId,
        InstanceId = "instance",
        TaskId = taskId,
        TokenId = tokenId,
    };

    /// <summary>
    /// 建立含两个审批分支和成对汇合的图。
    /// </summary>
    /// <param name="gatewayType">网关种类。</param>
    /// <returns>可执行图。</returns>
    internal static FlowExecutionGraph GatewayGraph(string gatewayType)
    {
        var graph = new FlowExecutionGraph
        {
            Nodes =
            [
                new() { Id = "start", Type = FlowNodeTypes.Start },
                new() { Id = "split", Type = gatewayType, GatewayDirection = FlowGatewayDirections.Split, GatewayPairId = "pair" },
                new() { Id = "a", Type = FlowNodeTypes.Approval },
                new() { Id = "b", Type = FlowNodeTypes.Approval },
                new() { Id = "join", Type = gatewayType, GatewayDirection = FlowGatewayDirections.Join, GatewayPairId = "pair" },
                new() { Id = "end", Type = FlowNodeTypes.End },
            ],
            Edges =
            [
                new() { Id = "start-split", SourceId = "start", TargetId = "split" },
                new() { Id = "split-a", SourceId = "split", TargetId = "a", Priority = 0, Condition = "${x} > 1" },
                new() { Id = "split-b", SourceId = "split", TargetId = "b", Priority = 1, IsDefault = true },
                new() { Id = "a-join", SourceId = "a", TargetId = "join" },
                new() { Id = "b-join", SourceId = "b", TargetId = "join" },
                new() { Id = "join-end", SourceId = "join", TargetId = "end" },
            ],
        };
        if (gatewayType == FlowNodeTypes.InclusiveGateway)
        {
            graph.Nodes.Insert(4, new FlowExecutionNode { Id = "fallback", Type = FlowNodeTypes.Approval });
            graph.Edges.Single(item => item.Id == "split-b").IsDefault = false;
            graph.Edges.Single(item => item.Id == "split-b").Condition = "${y} > 1";
            graph.Edges.Add(new FlowExecutionEdge { Id = "split-fallback", SourceId = "split", TargetId = "fallback", Priority = 2, IsDefault = true });
            graph.Edges.Add(new FlowExecutionEdge { Id = "fallback-join", SourceId = "fallback", TargetId = "join" });
        }

        return graph;
    }

    /// <summary>
    /// 建立可重复审批的排他分支回路。
    /// </summary>
    /// <returns>回路执行图。</returns>
    private static FlowExecutionGraph LoopGraph() => new()
    {
        Nodes =
        [
            new() { Id = "start", Type = FlowNodeTypes.Start },
            new() { Id = "approval", Type = FlowNodeTypes.Approval },
            new() { Id = "loop", Type = FlowNodeTypes.ExclusiveGateway, GatewayDirection = FlowGatewayDirections.Split },
            new() { Id = "end", Type = FlowNodeTypes.End },
        ],
        Edges =
        [
            new() { Id = "start-approval", SourceId = "start", TargetId = "approval" },
            new() { Id = "approval-loop", SourceId = "approval", TargetId = "loop" },
            new() { Id = "loop-back", SourceId = "loop", TargetId = "approval", Condition = "${again}", Priority = 0 },
            new() { Id = "loop-end", SourceId = "loop", TargetId = "end", IsDefault = true, Priority = 1 },
        ],
    };

    /// <summary>
    /// 建立外层并行、内层包容的嵌套分叉图。
    /// </summary>
    /// <returns>有两层分叉栈的执行图。</returns>
    private static FlowExecutionGraph NestedGatewayGraph() => new()
    {
        Nodes =
        [
            new() { Id = "start", Type = FlowNodeTypes.Start },
            new() { Id = "outerSplit", Type = FlowNodeTypes.ParallelGateway, GatewayDirection = FlowGatewayDirections.Split, GatewayPairId = "outer" },
            new() { Id = "innerSplit", Type = FlowNodeTypes.InclusiveGateway, GatewayDirection = FlowGatewayDirections.Split, GatewayPairId = "inner" },
            new() { Id = "innerA", Type = FlowNodeTypes.Approval },
            new() { Id = "innerB", Type = FlowNodeTypes.Approval },
            new() { Id = "innerFallback", Type = FlowNodeTypes.Approval },
            new() { Id = "innerJoin", Type = FlowNodeTypes.InclusiveGateway, GatewayDirection = FlowGatewayDirections.Join, GatewayPairId = "inner" },
            new() { Id = "outerB", Type = FlowNodeTypes.Approval },
            new() { Id = "outerJoin", Type = FlowNodeTypes.ParallelGateway, GatewayDirection = FlowGatewayDirections.Join, GatewayPairId = "outer" },
            new() { Id = "end", Type = FlowNodeTypes.End },
        ],
        Edges =
        [
            new() { Id = "start-outer", SourceId = "start", TargetId = "outerSplit" },
            new() { Id = "outer-a", SourceId = "outerSplit", TargetId = "innerSplit" },
            new() { Id = "outer-b", SourceId = "outerSplit", TargetId = "outerB" },
            new() { Id = "inner-a", SourceId = "innerSplit", TargetId = "innerA", Condition = "${x}" },
            new() { Id = "inner-b", SourceId = "innerSplit", TargetId = "innerB", Condition = "${y}" },
            new() { Id = "inner-default", SourceId = "innerSplit", TargetId = "innerFallback", IsDefault = true },
            new() { Id = "a-join", SourceId = "innerA", TargetId = "innerJoin" },
            new() { Id = "b-join", SourceId = "innerB", TargetId = "innerJoin" },
            new() { Id = "default-join", SourceId = "innerFallback", TargetId = "innerJoin" },
            new() { Id = "inner-outer", SourceId = "innerJoin", TargetId = "outerJoin" },
            new() { Id = "outerB-join", SourceId = "outerB", TargetId = "outerJoin" },
            new() { Id = "outer-end", SourceId = "outerJoin", TargetId = "end" },
        ],
    };

    /// <summary>
    /// 判断命令是否按冲突错误拒绝。
    /// </summary>
    /// <param name="action">待执行操作。</param>
    /// <returns>冲突时为真。</returns>
    private static bool ThrowsConflict(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (EngineException error)
        {
            return error.Code == ErrorCodes.Conflict;
        }
    }

    /// <summary>
    /// 判断命令是否按校验错误拒绝。
    /// </summary>
    /// <param name="action">待执行操作。</param>
    /// <returns>校验失败时为真。</returns>
    private static bool ThrowsValidation(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (EngineException error)
        {
            return error.Code == ErrorCodes.ValidationFailed;
        }
    }
}
