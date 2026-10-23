using ApprovalService.Contracts.Flows;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 固定种子的随机执行图与操作顺序检查；复现失败时只需保留种子和轮次。
/// </summary>
internal static class EngineFuzzChecks
{
    /// <summary>
    /// 运行超过一千次不同网关、节点和命令顺序的有界检查。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    public static void Run(Action<string, bool> check)
    {
        const int cases = 1200;
        var random = new Random(193711);
        var allCompleted = true;
        var noDuplicateAdvance = true;
        var tokenConservation = true;
        for (var index = 0; index < cases; index++)
        {
            var gateway = random.Next(3) switch
            {
                0 => FlowNodeTypes.ExclusiveGateway,
                1 => FlowNodeTypes.InclusiveGateway,
                _ => FlowNodeTypes.ParallelGateway,
            };
            var branches = random.Next(2, 6);
            var graph = CreateGraph(random, gateway, branches);
            var selected = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var edge in graph.Edges.Where(item => item.SourceId == "split" && !item.IsDefault))
            {
                selected[edge.Id] = random.Next(2) == 0;
            }

            var expected = gateway == FlowNodeTypes.ParallelGateway
                ? branches
                : gateway == FlowNodeTypes.ExclusiveGateway
                    ? 1
                    : Math.Max(1, selected.Values.Count(value => value));
            var context = new EngineContext
            {
                Graph = graph,
                OccurredAt = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero),
                Conditions = selected,
                Assignees = graph.Nodes.Where(node => node.Type is FlowNodeTypes.Approval or FlowNodeTypes.Handling or FlowNodeTypes.Cc)
                    .ToDictionary(node => node.Id, node => "user-" + node.Id, StringComparer.Ordinal),
            };
            var start = EngineRuntime.Execute(null, EngineScenarioChecks.Command(EngineCommandKinds.Start, "fuzz-" + index + "-start"), context);
            var events = new List<EngineEvent>(start.Events);
            var state = start.State;
            var operation = 0;
            while (state.Status == EngineInstanceStatuses.Running && operation < 20)
            {
                tokenConservation &= state.Tokens.Count <= branches;
                var actions = state.Tokens.Where(token => token.WaitingFor == "action").ToArray();
                var children = state.Tokens.Where(token => token.WaitingFor == "subprocess").ToArray();
                var tasks = state.Tasks.Where(task => task.Status == EngineTaskStatuses.Pending).ToArray();
                if (actions.Length + children.Length + tasks.Length == 0)
                {
                    allCompleted = false;
                    break;
                }

                var choice = random.Next(actions.Length + children.Length + tasks.Length);
                EngineCommand command;
                if (choice < actions.Length)
                {
                    command = EngineScenarioChecks.Command(EngineCommandKinds.ActionSucceeded,
                        "fuzz-" + index + "-" + operation, tokenId: actions[choice].Id);
                }
                else if (choice < actions.Length + children.Length)
                {
                    command = EngineScenarioChecks.Command(EngineCommandKinds.SubprocessCompleted,
                        "fuzz-" + index + "-" + operation, tokenId: children[choice - actions.Length].Id);
                }
                else
                {
                    var task = tasks[choice - actions.Length - children.Length];
                    var type = graph.Nodes.Single(node => node.Id == task.NodeId).Type;
                    var kind = type switch
                    {
                        FlowNodeTypes.Handling => EngineCommandKinds.Handle,
                        FlowNodeTypes.Cc => EngineCommandKinds.Acknowledge,
                        _ => EngineCommandKinds.Approve,
                    };
                    command = EngineScenarioChecks.Command(kind, "fuzz-" + index + "-" + operation, task.Id);
                }

                var result = EngineRuntime.Execute(state, command, context);
                state = result.State;
                events.AddRange(result.Events);
                operation++;
            }

            allCompleted &= state.Status == EngineInstanceStatuses.Approved && operation < 20;
            tokenConservation &= state.Tokens.Count == 0 && state.StepCount <= EngineLimits.MaxStepsPerInstance;
            var visits = events.Where(item => item.Kind == "nodeEntered" &&
                item.NodeId?.StartsWith("branch", StringComparison.Ordinal) == true).ToArray();
            noDuplicateAdvance &= visits.Length == expected &&
                visits.Select(item => item.NodeId).Distinct(StringComparer.Ordinal).Count() == expected &&
                events.Count(item => item.Kind == "gatewayJoined") == (gateway == FlowNodeTypes.ExclusiveGateway ? 0 : 1) &&
                events.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == events.Count;
        }

        check("固定种子 1200 次随机图和操作顺序均有界结束", allCompleted);
        check("随机图执行没有多余令牌或超出推进上限", tokenConservation);
        check("随机图没有重复处理分支任务", noDuplicateAdvance);
    }

    /// <summary>
    /// 生成一个有成对网关的有界随机图。
    /// </summary>
    /// <param name="random">固定种子的伪随机源。</param>
    /// <param name="gatewayType">网关类型。</param>
    /// <param name="branchCount">分支数。</param>
    /// <returns>执行图。</returns>
    private static FlowExecutionGraph CreateGraph(Random random, string gatewayType, int branchCount)
    {
        var graph = new FlowExecutionGraph
        {
            Nodes =
            [
                new() { Id = "start", Type = FlowNodeTypes.Start },
                new() { Id = "split", Type = gatewayType, GatewayDirection = FlowGatewayDirections.Split, GatewayPairId = "pair" },
                new() { Id = "join", Type = gatewayType, GatewayDirection = FlowGatewayDirections.Join, GatewayPairId = "pair" },
                new() { Id = "end", Type = FlowNodeTypes.End },
            ],
            Edges =
            [
                new() { Id = "start-split", SourceId = "start", TargetId = "split" },
                new() { Id = "join-end", SourceId = "join", TargetId = "end" },
            ],
        };
        for (var branch = 0; branch < branchCount; branch++)
        {
            var type = random.Next(5) switch
            {
                0 => FlowNodeTypes.Approval,
                1 => FlowNodeTypes.Handling,
                2 => FlowNodeTypes.Cc,
                3 => FlowNodeTypes.Automatic,
                _ => FlowNodeTypes.Subprocess,
            };
            var id = "branch" + branch;
            graph.Nodes.Add(new FlowExecutionNode
            {
                Id = id,
                Type = type,
                Approval = type == FlowNodeTypes.Cc ? new FlowApprovalSettings { ReadReceiptRequired = random.Next(2) == 0 } : null,
                Automation = type == FlowNodeTypes.Automatic ? new FlowAutomationSettings { ActionCode = "test" } : null,
                Subprocess = type == FlowNodeTypes.Subprocess ? new FlowSubprocessSettings
                {
                    FlowCode = "CHILD",
                    WaitForCompletion = random.Next(2) == 0,
                } : null,
            });
            graph.Edges.Add(new FlowExecutionEdge
            {
                Id = "split-" + branch,
                SourceId = "split",
                TargetId = id,
                Priority = branch,
                IsDefault = gatewayType != FlowNodeTypes.ParallelGateway && branch == branchCount - 1,
                Condition = branch == branchCount - 1 ? null : "${choice}",
            });
            graph.Edges.Add(new FlowExecutionEdge { Id = id + "-join", SourceId = id, TargetId = "join" });
        }

        return graph;
    }
}
