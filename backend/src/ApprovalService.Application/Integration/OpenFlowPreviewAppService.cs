using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 使用发布执行图和当前可信快照在内存中预计审批路径，不创建审批单。
/// </summary>
/// <param name="flows">已授权发布流程。</param>
/// <param name="organization">当前应用人员绑定。</param>
/// <param name="organizationRepository">候选人员的当前显示名称。</param>
/// <param name="contexts">发起时的组织与审批数据快照。</param>
/// <param name="businessAssignees">实际命中业务取人节点时的可信预取。</param>
/// <param name="timeProvider">可替换时钟。</param>
public sealed class OpenFlowPreviewAppService(FlowCatalogAppService flows,
    IOrganizationLookupAppService organization, IOrganizationRepository organizationRepository,
    OpenStartContextBuilder contexts,
    BusinessAssigneeAppService businessAssignees, TimeProvider timeProvider)
{
    /// <summary>
    /// 假设人工节点同意、自动动作和子流程成功，返回有界的预计路径与当前候选人。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="flowCode">要预览的发布流程编码。</param>
    /// <param name="request">与发起相同的人员选择和审批数据，不要求业务单号。</param>
    /// <param name="requestId">稳定请求 ID，用于纯引擎与业务取人去重。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>预计路径、候选人员与重算提示。</returns>
    public async Task<OpenFlowPreviewResponse> PreviewAsync(ClientAppEntity app, string flowCode,
        OpenFlowPreviewRequest request, string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (request is null || string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64 ||
            string.IsNullOrWhiteSpace(request.Initiator) || request.Initiator.Length > 128 ||
            request.Data.ValueKind != JsonValueKind.Object || request.SelectedAssignees is null ||
            request.BusinessKey?.Length > 200)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "流程预览必须提供已绑定发起人、数据对象和有效请求 ID。");
        }

        var published = await flows.RequirePublishedAsync(app, flowCode, cancellationToken);
        var initiatorId = await organization.ResolveBoundPersonAsync(app.Code, request.Initiator, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "发起人员未在当前接入应用绑定或已经离职，请先维护人员绑定。");
        var definition = JsonSerializer.Deserialize<FlowDefinition>(published.Version.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("已发布流程定义为空。");
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(published.Version.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("已发布执行图为空。");
        var data = OpenApprovalDataPreparer.Prepare(definition.Data, request.Data);
        var start = new OpenStartRequest
        {
            FlowCode = flowCode,
            BusinessKey = request.BusinessKey ?? string.Empty,
            Initiator = request.Initiator,
            InitiatingDepartment = request.InitiatingDepartment,
            Data = data,
            SelectedAssignees = request.SelectedAssignees,
        };
        var context = await contexts.BuildAsync(app.Code, definition, graph, start, initiatorId,
            data, timeProvider.GetUtcNow(), cancellationToken);
        var instance = new InstanceEntity
        {
            Id = "preview-" + requestId,
            FlowCode = flowCode,
            BusinessKey = request.BusinessKey ?? string.Empty,
            ClientAppCode = app.Code,
            ApprovalDataJson = data.GetRawText(),
            RoutingInputsJson = JsonSerializer.Serialize(new OpenRoutingInputs
            {
                InitiatorExternalId = request.Initiator,
                InitiatorSelections = context.AssigneeSnapshot?.InitiatorSelections ?? new(StringComparer.Ordinal),
                DataValues = context.AssigneeSnapshot?.DataValues ?? new(StringComparer.Ordinal),
            }, ApprovalJson.Options),
        };
        var path = new List<OpenPreviewNode>();
        EngineState? state = null;
        for (var step = 0; step < EngineLimits.MaxStepsPerInstance; step++)
        {
            var command = NextCommand(state, instance.Id, requestId, step);
            var requestedNodes = new HashSet<string>(StringComparer.Ordinal);
            EngineResult result;
            while (true)
            {
                try
                {
                    result = EngineRuntime.Execute(state, command, context);
                }
                catch (EngineException error)
                {
                    throw new UserFriendlyException(error.Code, error.Message);
                }

                if (result.RequiredBusinessAssignee is null)
                {
                    break;
                }

                // 缺少业务取人时原状态和事件均不保存；只向本次实际命中节点请求人员。
                if (!requestedNodes.Add(result.RequiredBusinessAssignee.NodeId) ||
                    requestedNodes.Count > graph.Nodes.Count)
                {
                    throw new InvalidOperationException("预览业务取人节点重复或超出执行图范围。");
                }

                await businessAssignees.PrepareAsync(instance, result.RequiredBusinessAssignee,
                    context, command.RequestId, cancellationToken);
            }

            state = result.State;
            for (var eventIndex = 0; eventIndex < result.Events.Count; eventIndex++)
            {
                var entered = result.Events[eventIndex];
                if (entered.Kind != "nodeEntered")
                {
                    continue;
                }

                var node = graph.Nodes.Single(item => item.Id == entered.NodeId);
                // 回路中同一令牌可再次进入同一节点，只归入这一次到达后创建的任务。
                var taskIds = result.Events.Skip(eventIndex + 1)
                    .TakeWhile(item => item.Kind != "nodeEntered" || item.TokenId != entered.TokenId)
                    .Where(item => item.Kind == "taskCreated" &&
                        item.TokenId == entered.TokenId && item.NodeId == entered.NodeId)
                    .Select(item => item.TaskId).ToHashSet(StringComparer.Ordinal);
                path.Add(new OpenPreviewNode
                {
                    NodeId = node.Id,
                    Name = node.Name,
                    Type = node.Type,
                    CandidateAssigneeIds = state.Tasks.Where(item => taskIds.Contains(item.Id))
                        .Select(item => item.AssigneeId).Distinct(StringComparer.Ordinal).ToList(),
                });
            }

            if (state.Status != EngineInstanceStatuses.Running)
            {
                await PopulateCandidateNamesAsync(path, cancellationToken);
                return new OpenFlowPreviewResponse
                {
                    FlowCode = flowCode,
                    Version = published.Version.Version,
                    Nodes = path,
                    ProjectedStatus = state.Status,
                    Notice = "按当前数据和组织、人工操作同意及自动动作成功估算；实际到达节点时会按最新组织和业务数据重新计算。",
                };
            }
        }

        throw new UserFriendlyException(ErrorCodes.ValidationFailed, "预计路径超过安全步数上限，请检查流程回路。");
    }

    /// <summary>
    /// 候选人当前显示名称与 ID 一起返回，避免业务系统无法识别审批服务内部 ID。
    /// </summary>
    /// <param name="path">已由纯引擎算出的节点路径。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    private async Task PopulateCandidateNamesAsync(List<OpenPreviewNode> path, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in path.SelectMany(node => node.CandidateAssigneeIds).Distinct(StringComparer.Ordinal))
        {
            var person = await organizationRepository.GetPersonAsync(id, cancellationToken);
            if (person is null || !person.IsActive)
            {
                throw new UserFriendlyException(ErrorCodes.Conflict,
                    "预览过程中候选审批人发生变化，请重新预览。");
            }

            names[id] = person.Name;
        }

        foreach (var node in path)
        {
            node.CandidateAssignees = node.CandidateAssigneeIds.Select(id => new OpenPreviewCandidate
            {
                Id = id,
                Name = names[id],
            }).ToList();
        }
    }

    /// <summary>
    /// 为下一步内存模拟选择一项待办或外部成功结论；不执行任何真实审批操作。
    /// </summary>
    /// <param name="state">上一步纯引擎状态。</param>
    /// <param name="instanceId">仅用于内存预览的审批单 ID。</param>
    /// <param name="requestId">本次预览请求 ID。</param>
    /// <param name="step">命令序号。</param>
    /// <returns>下一条纯引擎命令。</returns>
    private static EngineCommand NextCommand(EngineState? state, string instanceId, string requestId, int step)
    {
        var command = new EngineCommand
        {
            Kind = EngineCommandKinds.Start,
            InstanceId = instanceId,
            RequestId = requestId + "/preview/" + step,
        };
        if (state is null)
        {
            return command;
        }

        var task = state.Tasks.FirstOrDefault(item => item.Status == EngineTaskStatuses.Pending);
        if (task is not null)
        {
            command.Kind = task.CompletionKind;
            command.TaskId = task.Id;
            command.Operation = new()
            {
                ActorId = task.AssigneeId
            };
            return command;
        }

        var token = state.Tokens.FirstOrDefault(item => item.WaitingFor is "action" or "subprocess");
        if (token is not null)
        {
            command.Kind = token.WaitingFor == "action"
                ? EngineCommandKinds.ActionSucceeded : EngineCommandKinds.SubprocessCompleted;
            command.TokenId = token.Id;
            return command;
        }

        throw new UserFriendlyException(ErrorCodes.Conflict, "流程预览无法继续推进，请检查节点处理人和网关配置。");
    }
}
