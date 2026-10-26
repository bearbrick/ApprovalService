using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Definitions;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 用假输入驱动同一个纯引擎，全部结果只保存于进程内存。
/// </summary>
/// <param name="flows">复用现有流程管理范围授权。</param>
/// <param name="sessions">有界内存会话容器。</param>
/// <param name="timeProvider">可替换的时钟。</param>
public sealed class FlowSimulationAppService(FlowManagementAppService flows,
    IFlowSimulationSessionStore sessions, TimeProvider timeProvider)
{
    /// <summary>
    /// 会话闲置期限。
    /// </summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 在未保存草稿上启动一场假审批。
    /// </summary>
    /// <param name="request">流程草稿及假输入。</param>
    /// <param name="actorId">已验证本地管理员 ID。</param>
    /// <param name="isLocal">是否本地登录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>首次推进后的状态。</returns>
    public async Task<FlowSimulationResponse> CreateAsync(FlowSimulationCreateRequest request,
        string actorId, bool isLocal, CancellationToken cancellationToken = default)
    {
        if (request?.Definition is null || string.IsNullOrWhiteSpace(request.Definition.Code) ||
            request.Data.ValueKind != JsonValueKind.Object ||
            string.IsNullOrWhiteSpace(request.InitiatorId) || request.InitiatorId.Length > 128 ||
            request.FakeAssignees is null)
        {
            throw Validation("模拟需要完整流程草稿、假数据对象、假发起人与假处理人。");
        }

        // 权限必须按服务端当前的流程管理范围核对，不能信任客户端草稿里的管理员字段。
        await flows.GetAsync(request.Definition.Code, actorId, isLocal, cancellationToken);
        var compiled = FlowCompiler.Compile(request.Definition);
        if (compiled.Graph is null)
        {
            throw Validation("模拟草稿发布检查未通过：" +
                string.Join("；", compiled.Issues.Take(4).Select(item => item.Message)));
        }

        var graph = compiled.Graph;
        // 测试附件仅校验字段格式；假文件 ID 不查真实存储，也不关联任何审批单。
        var data = OpenApprovalDataPreparer.PrepareWithAttachments(request.Definition.Data, request.Data).Data;
        var snapshot = BuildFakeSnapshot(graph, request.InitiatorId, request.FakeAssignees);
        var conditions = FlowConditionEvaluator.Evaluate(request.Definition.Data, graph, data);
        var id = Guid.NewGuid().ToString("N");
        var now = timeProvider.GetUtcNow();
        var context = Context(graph, snapshot, conditions, request.Definition.Code, now);
        EngineResult started;
        try
        {
            started = EngineRuntime.Execute(null, new EngineCommand
            {
                Kind = EngineCommandKinds.Start,
                InstanceId = "simulation-" + id,
                RequestId = "start",
            }, context);
        }
        catch (EngineException error)
        {
            throw new UserFriendlyException(error.Code, error.Message);
        }

        var session = new FlowSimulationSession
        {
            Id = id,
            OwnerId = actorId,
            FlowCode = request.Definition.Code,
            Graph = graph,
            DataDefinition = request.Definition.Data,
            Data = data.Clone(),
            Assignees = snapshot,
            Conditions = conditions,
            State = started.State,
            Step = 1,
            ExpiresAt = now + IdleTimeout,
        };
        AppendEvents(session, started.Events);
        sessions.Add(session, now);
        return Response(session);
    }

    /// <summary>
    /// 读取自己的当前假审批，成功读取也刷新闲置期限。
    /// </summary>
    /// <param name="id">模拟会话 ID。</param>
    /// <param name="actorId">已验证本地管理员 ID。</param>
    /// <param name="isLocal">是否本地登录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>模拟状态。</returns>
    public async Task<FlowSimulationResponse> GetAsync(string id, string actorId, bool isLocal,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(id, actorId, isLocal, cancellationToken);
        lock (session.Sync)
        {
            Touch(session, timeProvider.GetUtcNow());
            return Response(session);
        }
    }

    /// <summary>
    /// 执行一条假人工处理或显式假外部结论。
    /// </summary>
    /// <param name="id">模拟会话 ID。</param>
    /// <param name="request">假命令。</param>
    /// <param name="actorId">已验证本地管理员 ID。</param>
    /// <param name="isLocal">是否本地登录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>本步后的状态。</returns>
    public async Task<FlowSimulationResponse> StepAsync(string id, FlowSimulationStepRequest request,
        string actorId, bool isLocal, CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(id, actorId, isLocal, cancellationToken);
        lock (session.Sync)
        {
            var now = timeProvider.GetUtcNow();
            EnsureFresh(session, now);
            if (request is null || session.State.Status != EngineInstanceStatuses.Running ||
                session.Step >= EngineLimits.MaxStepsPerInstance)
            {
                throw new UserFriendlyException(ErrorCodes.Conflict, "模拟已结束或达到步骤上限。");
            }

            var command = BuildCommand(session, request);
            var pendingValues = command.Kind is EngineCommandKinds.Approve or EngineCommandKinds.Handle or
                EngineCommandKinds.Acknowledge ? command.Operation?.FieldValues : null;
            var conditions = FlowConditionEvaluator.Evaluate(session.DataDefinition, session.Graph,
                session.Data, session.State.Operations.CurrentApproverFieldValues, pendingValues);
            var context = Context(session.Graph, session.Assignees, conditions, session.FlowCode, now);
            EngineResult result;
            try
            {
                result = EngineRuntime.Execute(session.State, command, context);
            }
            catch (EngineException error)
            {
                throw new UserFriendlyException(error.Code, error.Message);
            }

            if (result.RequiredBusinessAssignee is not null)
            {
                throw Validation("模拟缺少该节点的假处理人。");
            }

            session.State = result.State;
            session.Conditions = conditions;
            session.Step++;
            AppendEvents(session, result.Events);
            session.ExpiresAt = now + IdleTimeout;
            return Response(session);
        }
    }

    /// <summary>
    /// 显式结束并移除自己的会话。
    /// </summary>
    /// <param name="id">模拟会话 ID。</param>
    /// <param name="actorId">已验证本地管理员 ID。</param>
    /// <param name="isLocal">是否本地登录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public async Task DeleteAsync(string id, string actorId, bool isLocal,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(id, actorId, isLocal, cancellationToken);
        lock (session.Sync)
        {
            EnsureFresh(session, timeProvider.GetUtcNow());
            sessions.Remove(id);
        }
    }

    /// <summary>
    /// 仅会话创建人且仍有该流程管理权限时可以继续模拟。
    /// </summary>
    /// <param name="id">会话 ID。</param>
    /// <param name="actorId">当前人员 ID。</param>
    /// <param name="isLocal">是否本地账号。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>有效会话。</returns>
    private async Task<FlowSimulationSession> RequireAsync(string id, string actorId, bool isLocal,
        CancellationToken cancellationToken)
    {
        var session = sessions.Find(id);
        if (session is null || session.OwnerId != actorId)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "模拟会话不存在或已过期。");
        }

        await flows.GetAsync(session.FlowCode, actorId, isLocal, cancellationToken);
        return session;
    }

    /// <summary>
    /// 只接受当前等待中的任务和外部结论，防止客户端伪造其他引擎操作。
    /// </summary>
    /// <param name="session">当前会话。</param>
    /// <param name="request">待执行的假命令。</param>
    /// <returns>受限引擎命令。</returns>
    private static EngineCommand BuildCommand(FlowSimulationSession session, FlowSimulationStepRequest request)
    {
        var command = new EngineCommand
        {
            Kind = request.Kind,
            InstanceId = session.State.InstanceId,
            RequestId = "step-" + session.Step,
            TaskId = request.TaskId,
            TokenId = request.TokenId,
            Success = request.Success,
            ActionResult = request.ActionResult,
        };
        if (request.Kind is EngineCommandKinds.Approve or EngineCommandKinds.Handle or
            EngineCommandKinds.Acknowledge or EngineCommandKinds.Reject)
        {
            var task = session.State.Tasks.SingleOrDefault(item => item.Id == request.TaskId &&
                item.Status == EngineTaskStatuses.Pending);
            if (task is null || task.AssigneeId != request.ActorId ||
                (request.Kind != task.CompletionKind && !(task.CanReject && request.Kind == EngineCommandKinds.Reject)))
            {
                throw Validation("假操作人、任务或动作不匹配当前待办。");
            }

            EnsureFakeFields(session, task.NodeId, request.FieldValues);
            command.Operation = new OperationInput { ActorId = task.AssigneeId, FieldValues = request.FieldValues };
        }
        else if (request.Kind is EngineCommandKinds.ActionSucceeded or EngineCommandKinds.ActionFailed or
            EngineCommandKinds.SubprocessCompleted)
        {
            var expected = request.Kind == EngineCommandKinds.SubprocessCompleted ? "subprocess" : "action";
            if (!session.State.Tokens.Any(item => item.Id == request.TokenId && item.WaitingFor == expected))
            {
                throw Validation("该令牌没有等待这一类假外部结果。");
            }
        }
        else
        {
            throw Validation("测试模式只接受人工处理和显式假外部结论。");
        }

        return command;
    }

    /// <summary>
    /// 假填写也只允许当前节点可编辑的审批人字段，不接收业务字段或其他节点的字段。
    /// </summary>
    /// <param name="session">锁定的模拟定义。</param>
    /// <param name="nodeId">当前任务节点。</param>
    /// <param name="values">本次假填写。</param>
    private static void EnsureFakeFields(FlowSimulationSession session, string nodeId, JsonElement? values)
    {
        if (values is null)
        {
            return;
        }

        if (values.Value.ValueKind != JsonValueKind.Object)
        {
            throw Validation("假填写必须是审批人字段对象。");
        }

        var node = session.Graph.Nodes.Single(item => item.Id == nodeId);
        foreach (var field in values.Value.EnumerateObject())
        {
            if (!session.DataDefinition.Fields.Any(item => item.Key == field.Name && item.ApproverInput) ||
                !node.FieldPermissions.Any(item => item.FieldKey == field.Name &&
                    item.Access == FlowFieldAccess.Editable))
            {
                throw Validation($"节点 {nodeId} 无权填写字段 {field.Name}。");
            }
        }
    }

    /// <summary>
    /// 用假人员覆盖原取人来源，保留原多人审批策略和引擎规则。
    /// </summary>
    /// <param name="graph">本会话独有的执行图。</param>
    /// <param name="initiatorId">假发起人 ID。</param>
    /// <param name="fakeAssignees">各节点假人员。</param>
    /// <returns>纯假人员快照。</returns>
    private static AssigneeSnapshot BuildFakeSnapshot(FlowExecutionGraph graph, string initiatorId,
        Dictionary<string, List<string>> fakeAssignees)
    {
        var snapshot = new AssigneeSnapshot { InitiatorId = initiatorId };
        var manualNodes = graph.Nodes.Where(item => item.Type is FlowNodeTypes.Approval or
            FlowNodeTypes.Handling or FlowNodeTypes.Cc).ToArray();
        foreach (var node in manualNodes)
        {
            if (!fakeAssignees.TryGetValue(node.Id, out var people) || people is null ||
                people.Count is < 1 or > 50 || people.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128))
            {
                throw Validation($"节点 {node.Id} 缺少有效假处理人。");
            }

            snapshot.BusinessApiResults[node.Id] = people.Distinct(StringComparer.Ordinal).ToList();
            node.Assignee = new FlowAssignee { Source = FlowAssigneeSources.BusinessApi, Value = node.Id };
        }

        if (fakeAssignees.Keys.Any(id => manualNodes.All(node => node.Id != id)))
        {
            throw Validation("假处理人包含流程中不存在的人工节点。");
        }

        snapshot.People = fakeAssignees.Values.SelectMany(item => item).Append(initiatorId)
            .Distinct(StringComparer.Ordinal).Select(id => new AssigneePerson { Id = id }).ToList();
        return snapshot;
    }

    /// <summary>
    /// 构造完全不持有出站适配器的纯引擎上下文。
    /// </summary>
    /// <param name="graph">模拟图。</param>
    /// <param name="snapshot">假人员。</param>
    /// <param name="conditions">假数据分支条件。</param>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="now">本步时间。</param>
    /// <returns>纯计算上下文。</returns>
    private static EngineContext Context(FlowExecutionGraph graph, AssigneeSnapshot snapshot,
        Dictionary<string, bool> conditions, string flowCode, DateTimeOffset now) => new()
        {
            Graph = graph,
            OccurredAt = now,
            AssigneeSnapshot = snapshot,
            Conditions = conditions,
            Operations = new OperationContext
            {
                InitiatorId = snapshot.InitiatorId,
                FlowCode = flowCode,
                WithdrawBeforeFirstDecision = graph.WithdrawBeforeFirstDecision,
                ReturnMode = graph.ReturnMode,
            },
        };

    /// <summary>
    /// 追加本步纯引擎事件摘要，避免返回真实出站载荷。
    /// </summary>
    /// <param name="session">当前会话。</param>
    /// <param name="events">本步事件。</param>
    private static void AppendEvents(FlowSimulationSession session, IReadOnlyList<EngineEvent> events)
    {
        foreach (var item in events)
        {
            session.Events.Add(new FlowSimulationEvent
            {
                Kind = item.Kind,
                NodeId = item.NodeId,
                TaskId = item.TaskId,
                TokenId = item.TokenId,
            });
        }
    }

    /// <summary>
    /// 检查过期并刷新闲置期限。
    /// </summary>
    /// <param name="session">当前会话。</param>
    /// <param name="now">当前时间。</param>
    private void Touch(FlowSimulationSession session, DateTimeOffset now)
    {
        EnsureFresh(session, now);
        session.ExpiresAt = now + IdleTimeout;
    }

    /// <summary>
    /// 过期会话立即删除。
    /// </summary>
    /// <param name="session">当前会话。</param>
    /// <param name="now">当前时间。</param>
    private void EnsureFresh(FlowSimulationSession session, DateTimeOffset now)
    {
        if (!ReferenceEquals(sessions.Find(session.Id), session) || session.ExpiresAt <= now)
        {
            sessions.Remove(session.Id);
            throw new UserFriendlyException(ErrorCodes.NotFound, "模拟会话不存在或已过期。");
        }
    }

    /// <summary>
    /// 返回脱离可变引擎状态的响应副本。
    /// </summary>
    /// <param name="session">当前会话。</param>
    /// <returns>可序列化响应。</returns>
    private static FlowSimulationResponse Response(FlowSimulationSession session) => new()
    {
        Id = session.Id,
        FlowCode = session.FlowCode,
        ExpiresAt = session.ExpiresAt,
        Status = session.State.Status,
        Tasks = session.State.Tasks.Select(item => new FlowSimulationTask
        {
            Id = item.Id,
            NodeId = item.NodeId,
            AssigneeId = item.AssigneeId,
            Status = item.Status,
            CompletionKind = item.CompletionKind,
        }).ToList(),
        Waits = session.State.Tokens.Where(item => item.WaitingFor is "action" or "subprocess")
            .Select(item => new FlowSimulationWait
            {
                TokenId = item.Id,
                NodeId = item.NodeId,
                Kind = item.WaitingFor!,
            }).ToList(),
        Events = session.Events.Select(item => new FlowSimulationEvent
        {
            Kind = item.Kind,
            NodeId = item.NodeId,
            TaskId = item.TaskId,
            TokenId = item.TokenId,
        }).ToList(),
    };

    /// <summary>
    /// 可由管理员修正的假输入错误。
    /// </summary>
    /// <param name="message">中文原因。</param>
    /// <returns>统一 400 错误。</returns>
    private static UserFriendlyException Validation(string message) =>
        new(ErrorCodes.ValidationFailed, message);
}
