using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Engine.Execution;

/// <summary>
/// 单命令的临时计算区，负责令牌推进、网关同步和事件生成。
/// </summary>
internal sealed class EngineWork
{
    /// <summary>
    /// 本命令的状态副本。
    /// </summary>
    private readonly EngineState state;
    /// <summary>
    /// 本命令。
    /// </summary>
    private readonly EngineCommand command;
    /// <summary>
    /// 外部输入快照。
    /// </summary>
    private readonly EngineContext context;
    /// <summary>
    /// 本命令内的确定性 ID 序号。
    /// </summary>
    private int sequence;

    /// <summary>
    /// 建立单命令计算区。
    /// </summary>
    /// <param name="state">可修改的状态副本。</param>
    /// <param name="command">本命令。</param>
    /// <param name="context">执行上下文。</param>
    public EngineWork(EngineState state, EngineCommand command, EngineContext context)
    {
        this.state = state;
        this.command = command;
        this.context = context;
    }

    /// <summary>
    /// 本命令产生的事件。
    /// </summary>
    public List<EngineEvent> Events { get; } = [];

    /// <summary>
    /// 从调用方的请求 ID 派生稳定 ID，不调用随机源。
    /// </summary>
    /// <param name="kind">ID 用途。</param>
    /// <returns>本命令内唯一的 ID。</returns>
    public string NewId(string kind) => command.InstanceId + "/" + command.RequestId + "/" + kind + "/" + sequence++;

    /// <summary>
    /// 追加一条带调用方时间的事件。
    /// </summary>
    /// <param name="kind">事件种类。</param>
    /// <param name="nodeId">相关节点。</param>
    /// <param name="tokenId">相关令牌。</param>
    /// <param name="taskId">相关任务。</param>
    /// <param name="targetCode">外部目标编码。</param>
    public void Emit(string kind, string? nodeId = null, string? tokenId = null, string? taskId = null, string? targetCode = null) =>
        Events.Add(new EngineEvent
        {
            Id = NewId("event"),
            Kind = kind,
            OccurredAt = context.OccurredAt,
            NodeId = nodeId,
            TokenId = tokenId,
            TaskId = taskId,
            TargetCode = targetCode,
        });

    /// <summary>
    /// 将令牌沿唯一出线推进。
    /// </summary>
    /// <param name="token">要推进的令牌。</param>
    public void MoveSingle(EngineToken token)
    {
        var edge = context.Graph.Edges.SingleOrDefault(item => item.SourceId == token.NodeId)
            ?? throw new EngineException(ErrorCodes.ValidationFailed, "节点缺少唯一出线。");
        token.IncomingEdgeId = edge.Id;
        token.NodeId = edge.TargetId;
    }

    /// <summary>
    /// 推进全部可立即执行的令牌；等待人工、外部动作或汇合时停下。
    /// </summary>
    public void Advance()
    {
        while (state.Status == EngineInstanceStatuses.Running)
        {
            var token = state.Tokens.FirstOrDefault(item => item.WaitingFor is null);
            if (token is null)
            {
                return;
            }

            state.StepCount++;
            if (state.StepCount > EngineLimits.MaxStepsPerInstance)
            {
                Fault("stepLimitExceeded", token);
                return;
            }

            var node = context.Graph.Nodes.SingleOrDefault(item => item.Id == token.NodeId)
                ?? throw new EngineException(ErrorCodes.ValidationFailed, "令牌所指的节点不存在。");
            Emit("nodeEntered", node.Id, token.Id);
            switch (node.Type)
            {
                case FlowNodeTypes.Start:
                    MoveSingle(token);
                    break;
                case FlowNodeTypes.End:
                    state.Tokens.Remove(token);
                    if (state.Tokens.Count == 0)
                    {
                        state.Status = EngineInstanceStatuses.Approved;
                        Emit("instanceApproved", node.Id, token.Id);
                    }

                    break;
                case FlowNodeTypes.Approval:
                case FlowNodeTypes.Handling:
                    CreateTask(token, node);
                    break;
                case FlowNodeTypes.Cc:
                    if (node.Approval?.ReadReceiptRequired == true)
                    {
                        CreateTask(token, node);
                    }
                    else
                    {
                        Emit("ccDelivered", node.Id, token.Id);
                        MoveSingle(token);
                    }

                    break;
                case FlowNodeTypes.ExclusiveGateway:
                case FlowNodeTypes.InclusiveGateway:
                case FlowNodeTypes.ParallelGateway:
                    if (node.GatewayDirection == FlowGatewayDirections.Split)
                    {
                        Split(token, node);
                    }
                    else if (node.Type == FlowNodeTypes.ExclusiveGateway)
                    {
                        MoveSingle(token);
                    }
                    else
                    {
                        Join(token, node);
                    }

                    break;
                case FlowNodeTypes.Automatic:
                    token.WaitingFor = "action";
                    Emit("actionPending", node.Id, token.Id, targetCode: node.Automation?.ActionCode);
                    break;
                case FlowNodeTypes.Subprocess:
                    if (context.SubprocessDepth >= EngineLimits.MaxSubprocessDepth)
                    {
                        throw new EngineException(ErrorCodes.ValidationFailed, "子流程嵌套超过上限。");
                    }

                    Emit("subprocessRequested", node.Id, token.Id, targetCode: node.Subprocess?.FlowCode);
                    if (node.Subprocess?.WaitForCompletion != false)
                    {
                        token.WaitingFor = "subprocess";
                    }
                    else
                    {
                        MoveSingle(token);
                    }

                    break;
                default:
                    throw new EngineException(ErrorCodes.ValidationFailed, "执行图含不支持的节点类型。");
            }
        }
    }

    /// <summary>
    /// 标记无法自动恢复的异常并停止推进。
    /// </summary>
    /// <param name="kind">异常事件种类。</param>
    /// <param name="token">相关令牌。</param>
    public void Fault(string kind, EngineToken token)
    {
        state.Status = EngineInstanceStatuses.Faulted;
        Emit(kind, token.NodeId, token.Id);
    }

    /// <summary>
    /// 建立单人任务；处理人查询结果由调用方提前传入。
    /// </summary>
    /// <param name="token">任务令牌。</param>
    /// <param name="node">人工节点。</param>
    private void CreateTask(EngineToken token, FlowExecutionNode node)
    {
        if (!context.Assignees.TryGetValue(node.Id, out var assignee) || string.IsNullOrWhiteSpace(assignee))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "人工节点缺少已解析的处理人。");
        }

        var task = new EngineTask
        {
            Id = NewId("task"),
            TokenId = token.Id,
            NodeId = node.Id,
            AssigneeId = assignee,
        };
        state.Tasks.Add(task);
        token.WaitingFor = "task";
        Emit("taskCreated", node.Id, token.Id, task.Id);
    }

    /// <summary>
    /// 条件分叉选择满足的边；包容分叉仅在没有条件命中时使用兜底边。
    /// </summary>
    /// <param name="token">抵达分叉的令牌。</param>
    /// <param name="node">分叉网关。</param>
    private void Split(EngineToken token, FlowExecutionNode node)
    {
        var edges = context.Graph.Edges.Where(item => item.SourceId == node.Id)
            .OrderBy(item => item.Priority).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
        if (edges.Count < 2)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "分叉网关至少需要两条出线。");
        }

        List<FlowExecutionEdge> selected;
        if (node.Type == FlowNodeTypes.ParallelGateway)
        {
            selected = edges;
        }
        else
        {
            // 缺少求值结果不能默认为不命中，否则会误走兜底审批路径。
            foreach (var edge in edges.Where(item => !item.IsDefault))
            {
                if (!context.Conditions.ContainsKey(edge.Id))
                {
                    throw new EngineException(ErrorCodes.ValidationFailed, "条件网关缺少连线求值结果。");
                }
            }

            selected = edges.Where(edge => !edge.IsDefault && context.Conditions[edge.Id]).ToList();
            if (selected.Count == 0)
            {
                selected = edges.Where(edge => edge.IsDefault).ToList();
            }
            else if (node.Type == FlowNodeTypes.ExclusiveGateway)
            {
                selected = [selected[0]];
            }
        }

        if (selected.Count == 0)
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "条件网关没有可走的分支。");
        }

        state.Tokens.Remove(token);
        var forkId = NewId("fork");
        foreach (var edge in selected)
        {
            var branch = new EngineToken
            {
                Id = NewId("token"),
                NodeId = edge.TargetId,
                IncomingEdgeId = edge.Id,
                Forks = token.Forks.Select(frame => new EngineForkFrame
                {
                    ForkId = frame.ForkId,
                    PairId = frame.PairId,
                    BranchEdgeId = frame.BranchEdgeId,
                    ExpectedEdgeIds = [.. frame.ExpectedEdgeIds],
                }).ToList(),
            };
            if (node.Type != FlowNodeTypes.ExclusiveGateway)
            {
                branch.Forks.Add(new EngineForkFrame
                {
                    ForkId = forkId,
                    PairId = node.GatewayPairId ?? string.Empty,
                    BranchEdgeId = edge.Id,
                    ExpectedEdgeIds = selected.Select(item => item.Id).ToList(),
                });
            }

            state.Tokens.Add(branch);
        }

        Emit("gatewaySplit", node.Id, token.Id);
    }

    /// <summary>
    /// 按同一次分叉的实际激活边汇合，避免包容网关等待未选中的分支。
    /// </summary>
    /// <param name="token">抵达汇合的令牌。</param>
    /// <param name="node">汇合网关。</param>
    private void Join(EngineToken token, FlowExecutionNode node)
    {
        var frame = token.Forks.LastOrDefault();
        if (frame is null || frame.PairId != node.GatewayPairId)
        {
            Fault("unmatchedGateway", token);
            return;
        }

        token.WaitingFor = "join";
        var arrivals = state.Tokens.Where(item => item.NodeId == node.Id && item.WaitingFor == "join" &&
            item.Forks.LastOrDefault()?.ForkId == frame.ForkId).ToList();
        if (arrivals.Count != frame.ExpectedEdgeIds.Count ||
            arrivals.Select(item => item.Forks[^1].BranchEdgeId).Distinct(StringComparer.Ordinal).Count() != frame.ExpectedEdgeIds.Count)
        {
            return;
        }

        foreach (var arrival in arrivals)
        {
            state.Tokens.Remove(arrival);
        }

        var merged = new EngineToken
        {
            Id = NewId("token"),
            NodeId = node.Id,
            Forks = token.Forks.Take(token.Forks.Count - 1).Select(item => new EngineForkFrame
            {
                ForkId = item.ForkId,
                PairId = item.PairId,
                BranchEdgeId = item.BranchEdgeId,
                ExpectedEdgeIds = [.. item.ExpectedEdgeIds],
            }).ToList(),
        };
        MoveSingle(merged);
        state.Tokens.Add(merged);
        Emit("gatewayJoined", node.Id, merged.Id);
    }
}
