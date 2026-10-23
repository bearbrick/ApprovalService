using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Abstractions;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution.Behaviors;
using ApprovalService.Engine.Policies;

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
    /// 实际到达但尚未取得业务处理人的节点；调用方补齐后应从原状态重算。
    /// </summary>
    public BusinessAssigneeRequirement? RequiredBusinessAssignee
    {
        get; private set;
    }

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
            var decision = NodeBehaviorRegistry.Resolve(node.Type, context)
                .Decide(EngineNodeInputCopy.Create(state, token, context));
            if (decision.EventKind is not null)
            {
                Emit(decision.EventKind, node.Id, token.Id, targetCode: decision.TargetCode);
                if (node.Type == FlowNodeTypes.Cc && decision.EventKind == "ccDelivered")
                {
                    // 普通抄送不创建任务，必须把到达时确定的收件人固定在事件中。
                    Events[^1].ResolvedAssigneeIds.AddRange(ResolveCopyRecipients(node));
                }
            }

            switch (decision.Action)
            {
                case EngineNodeAction.Move:
                    MoveSingle(token);
                    break;
                case EngineNodeAction.Complete:
                    state.Tokens.Remove(token);
                    if (state.Tokens.Count == 0)
                    {
                        state.Status = EngineInstanceStatuses.Approved;
                        Emit("instanceApproved", node.Id, token.Id);
                    }

                    break;
                case EngineNodeAction.CreateTask:
                    CreateTask(token, node, decision);
                    break;
                case EngineNodeAction.Split:
                    Split(token, node, decision);
                    break;
                case EngineNodeAction.Join:
                    Join(token, node);
                    break;
                case EngineNodeAction.WaitForAction:
                    token.WaitingFor = "action";
                    break;
                case EngineNodeAction.WaitForSubprocess:
                    token.WaitingFor = "subprocess";
                    break;
                default:
                    throw new EngineException(ErrorCodes.ValidationFailed, "节点行为返回了不支持的动作。");
            }

            if (RequiredBusinessAssignee is not null)
            {
                // 本轮计算仅用于定位真正走到的节点，不能保存已有的临时推进。
                return;
            }
        }
    }

    /// <summary>
    /// 按当前组织快照解析普通抄送收件人，旧单人输入继续兼容。
    /// </summary>
    /// <param name="node">已经到达的抄送节点。</param>
    /// <returns>本次实际送达的内部人员 ID。</returns>
    private IReadOnlyList<string> ResolveCopyRecipients(FlowExecutionNode node)
    {
        if (state.Operations.AssigneeOverrides.TryGetValue(node.Id, out var overridden))
        {
            return [overridden];
        }

        if (context.AssigneeSnapshot is not null && node.Assignee is not null)
        {
            return AssigneeRuleEngine.Resolve(node, state, context.AssigneeSnapshot,
                context.AssigneeResolvers).AssigneeIds;
        }

        if (context.Assignees.TryGetValue(node.Id, out var assignee) && !string.IsNullOrWhiteSpace(assignee))
        {
            return [assignee];
        }

        throw new EngineException(ErrorCodes.ValidationFailed, "抄送节点缺少已解析的收件人。");
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
    /// 记录一人的投票并应用节点多人策略；节点未决时继续等待其余人员。
    /// </summary>
    /// <param name="task">本次处理的任务。</param>
    /// <param name="token">对应令牌。</param>
    /// <param name="node">当前人工节点。</param>
    /// <param name="commandKind">同意、办理完成、已阅或拒绝。</param>
    public void CompleteTask(EngineTask task, EngineToken token, FlowExecutionNode node, string commandKind)
    {
        state.Operations.HasHumanDecision = true;
        state.Operations.HumanDecisionCount++;
        task.Status = commandKind == EngineCommandKinds.Reject ? EngineTaskStatuses.Rejected : EngineTaskStatuses.Approved;
        if (task.Status == EngineTaskStatuses.Approved && node.Type == FlowNodeTypes.Approval)
        {
            state.ApprovedAssigneeIds.Add(task.AssigneeId);
            state.LastApprovedAssigneeId = task.AssigneeId;
        }

        Emit("taskCompleted", node.Id, token.Id, task.Id);
        var ballots = state.Tasks.Where(item => item.TokenId == token.Id)
            .Select(item => new ApprovalBallot
            {
                TaskId = item.Id,
                AssigneeId = item.AssigneeId,
                Status = item.Status,
            }).ToArray();
        var decision = node.Type == FlowNodeTypes.Approval
            ? ApprovalPolicyRegistry.Evaluate(node.Approval ?? new FlowApprovalSettings(), ballots, context.ApprovalPolicies)
            : new ApprovalPolicyDecision { Outcome = commandKind == EngineCommandKinds.Reject ? "rejected" : "approved" };
        decision = OperationSignerCommands.AdjustDecision(state, token.Id, decision);
        if (decision.ActivateTaskId is not null)
        {
            var next = state.Tasks.Single(item => item.Id == decision.ActivateTaskId);
            next.Status = EngineTaskStatuses.Pending;
            Emit("taskActivated", node.Id, token.Id, next.Id);
        }

        if (decision.Outcome == "pending")
        {
            return;
        }

        foreach (var pending in state.Tasks.Where(item => item.TokenId == token.Id &&
            item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued))
        {
            pending.Status = EngineTaskStatuses.Cancelled;
            Emit("taskCancelled", node.Id, token.Id, pending.Id);
        }

        if (decision.Outcome == "rejected")
        {
            foreach (var pending in state.Tasks.Where(item => item.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued))
            {
                pending.Status = EngineTaskStatuses.Cancelled;
            }

            state.Tokens.Clear();
            state.Status = EngineInstanceStatuses.Rejected;
            Emit("instanceRejected", node.Id, token.Id, task.Id);
            return;
        }

        token.WaitingFor = null;
        MoveSingle(token);
        Advance();
    }

    /// <summary>
    /// 到达节点时按最新快照找人并建立单人或多人任务。
    /// </summary>
    /// <param name="token">任务令牌。</param>
    /// <param name="node">人工节点。</param>
    /// <param name="decision">节点行为指定的完成命令与拒绝权限。</param>
    private void CreateTask(EngineToken token, FlowExecutionNode node, EngineNodeDecision decision)
    {
        if (decision.TaskCompletionKind is not (EngineCommandKinds.Approve or EngineCommandKinds.Handle or EngineCommandKinds.Acknowledge))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "任务节点缺少受支持的完成命令。");
        }

        AssigneePlan plan;
        if (context.AssigneeSnapshot is not null && node.Assignee is not null)
        {
            if (node.Assignee.Source == FlowAssigneeSources.BusinessApi &&
                !context.AssigneeSnapshot.BusinessApiResults.ContainsKey(node.Id) &&
                (node.Assignee.Value is null ||
                    !context.AssigneeSnapshot.BusinessApiResults.ContainsKey(node.Assignee.Value)))
            {
                RequiredBusinessAssignee = new BusinessAssigneeRequirement
                {
                    NodeId = node.Id,
                    SourceValue = node.Assignee.Value,
                };
                return;
            }

            plan = AssigneeRuleEngine.Resolve(node, state, context.AssigneeSnapshot, context.AssigneeResolvers);
        }
        else if (context.Assignees.TryGetValue(node.Id, out var assignee) && !string.IsNullOrWhiteSpace(assignee))
        {
            // 兼容最小链路：调用方直接提供单人处理人。
            plan = new AssigneePlan { AssigneeIds = [assignee] };
        }
        else
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "人工节点缺少已解析的处理人。");
        }

        if (state.Operations.AssigneeOverrides.TryGetValue(node.Id, out var overrideId))
        {
            plan = new AssigneePlan { AssigneeIds = [overrideId] };
            Emit("assigneeOverridden", node.Id, token.Id);
        }

        if (context.PreviewAssignees.TryGetValue(node.Id, out var preview) &&
            !preview.SequenceEqual(plan.AssigneeIds, StringComparer.Ordinal))
        {
            Emit("assigneesChangedSincePreview", node.Id, token.Id);
            Events[^1].PreviewAssigneeIds = [.. preview];
            Events[^1].ResolvedAssigneeIds = [.. plan.AssigneeIds];
        }

        if (plan.AutoApproveReason is not null)
        {
            Emit(plan.AutoApproveReason, node.Id, token.Id);
            MoveSingle(token);
            return;
        }

        state.Operations.Visits.Add(new OperationVisit
        {
            NodeId = node.Id,
            Forks = OperationStateCopy.CopyForks(token.Forks),
        });
        var resolved = plan.AssigneeIds.Select(item => OperationDelegationResolver.Resolve(item, node, context))
            .Where(item => item.AssigneeId is not null).ToArray();
        if (resolved.Length == 0)
        {
            Emit("delegatedSelfApprovalSkipped", node.Id, token.Id);
            MoveSingle(token);
            return;
        }

        var sequential = node.Type == FlowNodeTypes.Approval &&
            (node.Approval?.Mode ?? FlowApprovalModes.Sequential) == FlowApprovalModes.Sequential;
        for (var index = 0; index < resolved.Length; index++)
        {
            var task = new EngineTask
            {
                Id = NewId("task"),
                TokenId = token.Id,
                NodeId = node.Id,
                AssigneeId = resolved[index].AssigneeId!,
                DelegatorId = resolved[index].DelegatorId,
                Status = sequential && index > 0 ? EngineTaskStatuses.Queued : EngineTaskStatuses.Pending,
                CompletionKind = decision.TaskCompletionKind,
                CanReject = decision.CanReject,
            };
            state.Tasks.Add(task);
            Emit("taskCreated", node.Id, token.Id, task.Id);
        }

        token.WaitingFor = "task";
    }

    /// <summary>
    /// 按节点行为选中的连线分叉，必要时记录本次分叉的汇合范围。
    /// </summary>
    /// <param name="token">抵达分叉的令牌。</param>
    /// <param name="node">分叉网关。</param>
    /// <param name="decision">节点行为的选线决定。</param>
    private void Split(EngineToken token, FlowExecutionNode node, EngineNodeDecision decision)
    {
        var outgoing = context.Graph.Edges.Where(item => item.SourceId == node.Id)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (decision.SelectedEdgeIds.Count == 0 ||
            decision.SelectedEdgeIds.Distinct(StringComparer.Ordinal).Count() != decision.SelectedEdgeIds.Count ||
            decision.SelectedEdgeIds.Any(edgeId => !outgoing.ContainsKey(edgeId)))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "节点行为选择了无效的分叉连线。");
        }

        var selected = decision.SelectedEdgeIds.Select(edgeId => outgoing[edgeId]).ToArray();

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
            if (decision.JoinPairId is not null)
            {
                branch.Forks.Add(new EngineForkFrame
                {
                    ForkId = forkId,
                    PairId = decision.JoinPairId,
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
