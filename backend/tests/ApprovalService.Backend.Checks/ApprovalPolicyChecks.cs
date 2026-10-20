using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Execution;
using ApprovalService.Engine.Policies;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 多人审批与自动处理的端到端引擎场景。
/// </summary>
internal static class ApprovalPolicyChecks
{
    /// <summary>
    /// 运行多人方式、缺人、本人、重复、停用和动态取人检查。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        CheckModes(check);
        CheckMissingAndSelf(check);
        CheckDuplicateAndInactive(check);
        CheckArrivalResolution(check);
        CheckInvalidSettings(check);
        CheckExtensionIsolation(check);
    }

    /// <summary>
    /// 检查依次、会签、或签、票签的任务推进与提前结束。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckModes(Action<string, bool> check)
    {
        var sequential = Start(Chain(Node("a", "member", "p1,p2,p3", new FlowApprovalSettings())));
        check("依次审批只激活第一人", sequential.State.Tasks.Select(item => item.Status)
            .SequenceEqual([EngineTaskStatuses.Pending, EngineTaskStatuses.Queued, EngineTaskStatuses.Queued]));
        var first = Act(sequential, EngineCommandKinds.Approve, 0, "seq-one");
        check("依次审批同意后激活下一人", first.State.Tasks[1].Status == EngineTaskStatuses.Pending &&
            first.State.Tasks[2].Status == EngineTaskStatuses.Queued && first.State.Status == EngineInstanceStatuses.Running);
        var refused = Act(first, EngineCommandKinds.Reject, 1, "seq-reject");
        check("依次审批拒绝后取消排队任务", refused.State.Status == EngineInstanceStatuses.Rejected &&
            refused.State.Tasks[2].Status == EngineTaskStatuses.Cancelled);

        var all = Start(Chain(Node("a", "member", "p1,p2", new FlowApprovalSettings { Mode = "all" })));
        var allRejected = Act(all, EngineCommandKinds.Reject, 0, "all-reject");
        check("会签默认有人拒绝立即结束", allRejected.State.Status == EngineInstanceStatuses.Rejected &&
            allRejected.State.Tasks[1].Status == EngineTaskStatuses.Cancelled);
        var waitAll = Start(Chain(Node("a", "member", "p1,p2", new FlowApprovalSettings
        {
            Mode = "all",
            RejectBehavior = "waitAll",
        })));
        var waitFirst = Act(waitAll, EngineCommandKinds.Reject, 0, "all-wait-first");
        var waitLast = Act(waitFirst, EngineCommandKinds.Approve, 1, "all-wait-last");
        check("会签可等全员处理后按反对结果拒绝", waitFirst.State.Status == EngineInstanceStatuses.Running &&
            waitLast.State.Status == EngineInstanceStatuses.Rejected);

        var any = Start(Chain(Node("a", "member", "p1,p2", new FlowApprovalSettings { Mode = "any" })));
        var anyApproved = Act(any, EngineCommandKinds.Approve, 0, "any-one");
        check("或签一人同意即通过并取消其他任务", anyApproved.State.Status == EngineInstanceStatuses.Approved &&
            anyApproved.State.Tasks[1].Status == EngineTaskStatuses.Cancelled);
        var anyRejected = Act(any, EngineCommandKinds.Reject, 0, "any-no-one");
        var anyAllRejected = Act(anyRejected, EngineCommandKinds.Reject, 1, "any-no-two");
        check("或签全部反对才拒绝", anyRejected.State.Status == EngineInstanceStatuses.Running &&
            anyAllRejected.State.Status == EngineInstanceStatuses.Rejected);

        var voteSettings = new FlowApprovalSettings
        {
            Mode = "vote",
            VoteThreshold = 0.6m,
            VoteWeights = new Dictionary<string, decimal> { ["p1"] = 2m },
        };
        var vote = Start(Chain(Node("a", "member", "p1,p2,p3", voteSettings)));
        var voteFirst = Act(vote, EngineCommandKinds.Approve, 0, "vote-first");
        var votePass = Act(voteFirst, EngineCommandKinds.Approve, 1, "vote-pass");
        check("票签以权重算比例，达标后取消剩余任务", voteFirst.State.Status == EngineInstanceStatuses.Running &&
            votePass.State.Status == EngineInstanceStatuses.Approved &&
            votePass.State.Tasks[2].Status == EngineTaskStatuses.Cancelled);
        var voteRejected = Act(vote, EngineCommandKinds.Reject, 0, "vote-reject");
        check("票签剩余权重不可能达标时提前拒绝", voteRejected.State.Status == EngineInstanceStatuses.Rejected &&
            voteRejected.State.Tasks.Skip(1).All(item => item.Status == EngineTaskStatuses.Cancelled));
    }

    /// <summary>
    /// 检查缺人和发起人本人处理选项。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckMissingAndSelf(Action<string, bool> check)
    {
        var snapshot = Snapshot();
        check("缺人默认阻止推进并给可纠正错误", ThrowsValidation(() => Start(Chain(
            Node("a", "role", "absent", new FlowApprovalSettings())), snapshot)));
        var missingApprove = Start(Chain(Node("a", "role", "absent", new FlowApprovalSettings
        {
            MissingAssigneeBehavior = "approve",
        })), snapshot);
        check("缺人可自动通过并留事件", missingApprove.State.Status == EngineInstanceStatuses.Approved &&
            missingApprove.Events.Any(item => item.Kind == "missingAssigneeApproved"));
        var administrator = Start(Chain(Node("a", "role", "absent", new FlowApprovalSettings
        {
            MissingAssigneeBehavior = "administrator",
        })), snapshot);
        check("缺人可转流程管理员", administrator.State.Tasks.Single().AssigneeId == "admin");
        var specified = Start(Chain(Node("a", "role", "absent", new FlowApprovalSettings
        {
            MissingAssigneeBehavior = "specified",
            MissingAssigneeUserId = "p3",
        })), snapshot);
        check("缺人可转节点指定人员", specified.State.Tasks.Single().AssigneeId == "p3");

        var selfSkipped = Start(Chain(Node("a", "initiator", null, new FlowApprovalSettings())), snapshot);
        check("发起人本人默认自动跳过", selfSkipped.State.Status == EngineInstanceStatuses.Approved &&
            selfSkipped.Events.Any(item => item.Kind == "assigneeSkipped"));
        var selfHandled = Start(Chain(Node("a", "initiator", null, new FlowApprovalSettings
        {
            SelfApprovalBehavior = "self",
        })), snapshot);
        check("可设置由发起人本人审批", selfHandled.State.Tasks.Single().AssigneeId == "initiator");
        var selfManager = Start(Chain(Node("a", "initiator", null, new FlowApprovalSettings
        {
            SelfApprovalBehavior = "manager",
        })), snapshot);
        check("发起人本人可转直属上级", selfManager.State.Tasks.Single().AssigneeId == "p1");
    }

    /// <summary>
    /// 检查重复处理人与停用人员的所有选项。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckDuplicateAndInactive(Action<string, bool> check)
    {
        var defaultDuplicate = Start(Chain(Node("a", "member", "p1"), Node("b", "member", "p1")));
        var adjacent = Act(defaultDuplicate, EngineCommandKinds.Approve, 0, "adjacent");
        check("同一人相邻默认只审批一次", adjacent.State.Status == EngineInstanceStatuses.Approved &&
            adjacent.Events.Any(item => item.Kind == "assigneeSkipped"));

        var onceSettings = new FlowApprovalSettings { DuplicateAssigneeBehavior = "once" };
        var once = Start(Chain(Node("a", "member", "p1"), Node("b", "member", "p2"),
            Node("c", "member", "p1", onceSettings)));
        once = Act(once, EngineCommandKinds.Approve, 0, "once-a");
        once = Act(once, EngineCommandKinds.Approve, 1, "once-b");
        check("全流程只审一次跨非相邻节点生效", once.State.Status == EngineInstanceStatuses.Approved &&
            once.Events.Any(item => item.Kind == "assigneeSkipped"));

        var everySettings = new FlowApprovalSettings { DuplicateAssigneeBehavior = "everyTime" };
        var every = Start(Chain(Node("a", "member", "p1"), Node("b", "member", "p1", everySettings)));
        every = Act(every, EngineCommandKinds.Approve, 0, "every-a");
        check("每次都审会再次生成任务", every.State.Status == EngineInstanceStatuses.Running &&
            every.State.Tasks.Count == 2 && every.State.Tasks[1].Status == EngineTaskStatuses.Pending);

        var snapshot = Snapshot();
        snapshot.People.Single(item => item.Id == "p2").IsActive = false;
        snapshot.People.Single(item => item.Id == "p2").ManagerId = "p1";
        var inactiveManager = Start(Chain(Node("a", "member", "p2")), snapshot);
        check("停用审批人默认转直属上级", inactiveManager.State.Tasks.Single().AssigneeId == "p1");
        var inactiveAdmin = Start(Chain(Node("a", "member", "p2", new FlowApprovalSettings
        {
            InactiveAssigneeBehavior = "administrator",
        })), snapshot);
        check("停用审批人可转流程管理员", inactiveAdmin.State.Tasks.Single().AssigneeId == "admin");
    }

    /// <summary>
    /// 检查人员确实在到节点时解析，并记录与预览的差异。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckArrivalResolution(Action<string, bool> check)
    {
        var graph = Chain(Node("a", "member", "p1"), Node("b", "role", "reviewer"));
        var before = Snapshot();
        before.People.Single(item => item.Id == "p2").Roles.Add("reviewer");
        var started = Start(graph, before);
        var after = Snapshot();
        after.People.Single(item => item.Id == "p3").Roles.Add("reviewer");
        var context = Context(graph, after);
        context.PreviewAssignees = new Dictionary<string, IReadOnlyList<string>> { ["b"] = ["p2"] };
        var reached = EngineRuntime.Execute(started.State,
            new EngineCommand { Kind = EngineCommandKinds.Approve, InstanceId = "i", RequestId = "arrival", TaskId = started.State.Tasks[0].Id },
            context);
        var change = reached.Events.SingleOrDefault(item => item.Kind == "assigneesChangedSincePreview");
        check("审批人到节点才重算，预览差异写事件", reached.State.Tasks[^1].AssigneeId == "p3" &&
            change is not null && change.PreviewAssigneeIds.SequenceEqual(["p2"]) &&
            change.ResolvedAssigneeIds.SequenceEqual(["p3"]));
    }

    /// <summary>
    /// 无效编码须明确报错，不能默认为其他审批行为。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckInvalidSettings(Action<string, bool> check)
    {
        check("无效缺人设置不会被当作默认阻止", ThrowsValidation(() => Start(Chain(
            Node("a", "member", "p1", new FlowApprovalSettings { MissingAssigneeBehavior = "unknown" })))));
        check("无效本人设置不会被当作本人审批", ThrowsValidation(() => Start(Chain(
            Node("a", "member", "p1", new FlowApprovalSettings { SelfApprovalBehavior = "unknown" })))));
        check("无效重复设置不会被当作每次审批", ThrowsValidation(() => Start(Chain(
            Node("a", "member", "p1", new FlowApprovalSettings { DuplicateAssigneeBehavior = "unknown" })))));
        check("无效停用设置不会被当作转上级", ThrowsValidation(() => Start(Chain(
            Node("a", "member", "p1", new FlowApprovalSettings { InactiveAssigneeBehavior = "unknown" })))));
        var all = Start(Chain(Node("a", "member", "p1,p2", new FlowApprovalSettings
        {
            Mode = "all",
            RejectBehavior = "unknown",
        })));
        check("无效会签拒绝设置不会被当成立即结束", ThrowsValidation(() => Act(all,
            EngineCommandKinds.Reject, 0, "bad-reject-setting")));
    }

    /// <summary>
    /// 扩展策略只能修改自己的副本，不能改变调用方设置和票据。
    /// </summary>
    /// <param name="check">结果记录器。</param>
    private static void CheckExtensionIsolation(Action<string, bool> check)
    {
        var settings = new FlowApprovalSettings { Mode = "custom" };
        var ballots = new[] { new ApprovalBallot
        {
            TaskId = "task",
            AssigneeId = "p1",
            Status = EngineTaskStatuses.Pending,
        } };
        var result = ApprovalPolicyRegistry.Evaluate(settings, ballots, [new MutatingPolicy()]);
        check("自定义多人策略不能修改原设置和任务票据", result.Outcome == "approved" &&
            settings.Mode == "custom" && ballots[0].Status == EngineTaskStatuses.Pending);
    }

    /// <summary>
    /// 故意修改策略输入以检查副本隔离。
    /// </summary>
    private sealed class MutatingPolicy : IApprovalPolicy
    {
        /// <inheritdoc />
        public string Mode => "custom";

        /// <inheritdoc />
        public ApprovalPolicyDecision Evaluate(FlowApprovalSettings settings, IReadOnlyList<ApprovalBallot> ballots)
        {
            settings.Mode = "changed";
            ballots[0].Status = EngineTaskStatuses.Approved;
            return new ApprovalPolicyDecision { Outcome = "approved" };
        }
    }

    /// <summary>
    /// 创建带完整发起人组织的快照。
    /// </summary>
    /// <returns>示例快照。</returns>
    private static AssigneeSnapshot Snapshot() => new()
    {
        InitiatorId = "initiator",
        InitiatingDepartmentId = "d1",
        AdministratorId = "admin",
        People =
        [
            new() { Id = "initiator", ManagerId = "p1", DepartmentIds = ["d1"] },
            new() { Id = "p1", DepartmentIds = ["d1"] },
            new() { Id = "p2", DepartmentIds = ["d1"] },
            new() { Id = "p3", DepartmentIds = ["d1"] },
            new() { Id = "admin", DepartmentIds = ["d1"] },
        ],
        Departments = [new() { Id = "d1", HeadId = "p1" }],
    };

    /// <summary>
    /// 建立一个审批节点。
    /// </summary>
    /// <param name="id">节点 ID。</param>
    /// <param name="source">人员来源。</param>
    /// <param name="value">来源配置值。</param>
    /// <param name="settings">节点规则。</param>
    /// <returns>执行节点。</returns>
    private static FlowExecutionNode Node(string id, string source, string? value,
        FlowApprovalSettings? settings = null) => new()
        {
            Id = id,
            Type = FlowNodeTypes.Approval,
            Assignee = new FlowAssignee { Source = source, Value = value },
            Approval = settings ?? new FlowApprovalSettings(),
        };

    /// <summary>
    /// 把人工节点接成从开始到结束的顺序图。
    /// </summary>
    /// <param name="nodes">中间节点。</param>
    /// <returns>执行图。</returns>
    private static FlowExecutionGraph Chain(params FlowExecutionNode[] nodes)
    {
        var all = new[] { new FlowExecutionNode { Id = "start", Type = FlowNodeTypes.Start } }
            .Concat(nodes).Concat([new FlowExecutionNode { Id = "end", Type = FlowNodeTypes.End }]).ToArray();
        return new FlowExecutionGraph
        {
            Nodes = [.. all],
            Edges = Enumerable.Range(0, all.Length - 1).Select(index => new FlowExecutionEdge
            {
                Id = "e" + index,
                SourceId = all[index].Id,
                TargetId = all[index + 1].Id,
            }).ToList(),
        };
    }

    /// <summary>
    /// 用固定时间执行开始命令。
    /// </summary>
    /// <param name="graph">执行图。</param>
    /// <param name="snapshot">人员快照。</param>
    /// <returns>执行结果。</returns>
    private static EngineResult Start(FlowExecutionGraph graph, AssigneeSnapshot? snapshot = null) =>
        EngineRuntime.Execute(null, new EngineCommand
        {
            Kind = EngineCommandKinds.Start,
            InstanceId = "i",
            RequestId = "start",
        }, Context(graph, snapshot ?? Snapshot()));

    /// <summary>
    /// 执行对指定序号任务的动作。
    /// </summary>
    /// <param name="previous">上一步结果。</param>
    /// <param name="kind">动作种类。</param>
    /// <param name="taskIndex">任务序号。</param>
    /// <param name="requestId">新请求 ID。</param>
    /// <returns>执行结果。</returns>
    private static EngineResult Act(EngineResult previous, string kind, int taskIndex, string requestId) =>
        EngineRuntime.Execute(previous.State, new EngineCommand
        {
            Kind = kind,
            InstanceId = "i",
            RequestId = requestId,
            TaskId = previous.State.Tasks[taskIndex].Id,
        }, Context(ChainFromState(previous.State), Snapshot()));

    /// <summary>
    /// 从检查上下文取出当前执行图；检查用实例状态不持有图。
    /// </summary>
    /// <param name="state">当前状态。</param>
    /// <returns>原执行图。</returns>
    private static FlowExecutionGraph ChainFromState(EngineState state) => currentGraph
        ?? throw new InvalidOperationException("检查未设置执行图。");

    /// <summary>
    /// 当前检查场景的执行图。
    /// </summary>
    private static FlowExecutionGraph? currentGraph;

    /// <summary>
    /// 构造引擎上下文并保存本检查的图。
    /// </summary>
    /// <param name="graph">执行图。</param>
    /// <param name="snapshot">人员快照。</param>
    /// <returns>固定上下文。</returns>
    private static EngineContext Context(FlowExecutionGraph graph, AssigneeSnapshot snapshot)
    {
        currentGraph = graph;
        return new EngineContext
        {
            Graph = graph,
            AssigneeSnapshot = snapshot,
            OccurredAt = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero),
        };
    }

    /// <summary>
    /// 判断操作是否抛出可纠正的校验错误。
    /// </summary>
    /// <param name="action">待执行动作。</param>
    /// <returns>是否是校验错误。</returns>
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
