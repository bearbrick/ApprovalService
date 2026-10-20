using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Assignees;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 仅从组织仓储和已发布定义生成引擎快照，不信任业务系统自造的人员、条件或矩阵命中。
/// </summary>
/// <param name="organization">组织主数据与严格绑定查询。</param>
/// <param name="organizationRepository">可信组织主数据查询。</param>
/// <param name="matrices">管理端维护的矩阵命中查询。</param>
public sealed class OpenStartContextBuilder(IOrganizationLookupAppService organization,
    IOrganizationRepository organizationRepository, IMatrixMatchProvider matrices)
{
    /// <summary>
    /// 为一次发起固定组织、审批数据、分支条件和自选处理人。
    /// </summary>
    /// <param name="appCode">已认证接入应用编码。</param>
    /// <param name="definition">已发布定义。</param>
    /// <param name="graph">对应的已发布执行图。</param>
    /// <param name="request">经基础校验的发起请求。</param>
    /// <param name="initiatorId">严格绑定后的内部发起人 ID。</param>
    /// <param name="data">服务端计算后的审批数据。</param>
    /// <param name="now">本次固定的发生时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>可交给纯引擎的快照。</returns>
    public async Task<EngineContext> BuildAsync(string appCode, FlowDefinition definition, FlowExecutionGraph graph,
        OpenStartRequest request, string initiatorId, JsonElement data, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await organization.LoadAssigneeSnapshotAsync(initiatorId, cancellationToken);
        var initiator = snapshot.People.SingleOrDefault(person => person.Id == initiatorId && person.IsActive)
            ?? throw new UserFriendlyException(ErrorCodes.Forbidden, "发起人员已停用或不在组织架构中。");
        if (request.InitiatingDepartment is not null)
        {
            if (string.IsNullOrWhiteSpace(request.InitiatingDepartment))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "发起部门 ID 不能为空。");
            }

            snapshot.InitiatingDepartmentId = await organization.ResolveBoundDepartmentAsync(appCode,
                request.InitiatingDepartment, cancellationToken)
                ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "发起部门未在当前接入应用绑定或已不存在，请先维护部门绑定。");
        }

        if ((request.InitiatingDepartment is not null || snapshot.InitiatingDepartmentId.Length > 0) &&
            !initiator.DepartmentIds.Contains(snapshot.InitiatingDepartmentId, StringComparer.Ordinal))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "发起人不属于所选发起部门。");
        }

        await CheckInitiatorScopeAsync(definition, initiator, snapshot.InitiatingDepartmentId, cancellationToken);
        if (request.SelectedAssignees is null)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "自选审批人格式不正确。");
        }

        var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var selection in request.SelectedAssignees)
        {
            if (!nodeById.TryGetValue(selection.Key, out var node) ||
                node.Assignee?.Source != FlowAssigneeSources.InitiatorSelection || selection.Value is null)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"自选审批人节点 {selection.Key} 不存在或未允许发起人自选。");
            }

            var people = new List<string>();
            foreach (var externalId in selection.Value)
            {
                people.Add(await RequireBoundPersonAsync(appCode, externalId, cancellationToken));
            }

            snapshot.InitiatorSelections[selection.Key] = people.Distinct(StringComparer.Ordinal).ToList();
        }

        foreach (var node in graph.Nodes)
        {
            var assignee = node.Assignee;
            if (assignee is null)
            {
                continue;
            }

            if (assignee.Source == FlowAssigneeSources.DataPerson)
            {
                var externalId = ReadField(data, assignee.FieldKey);
                if (externalId is not null)
                {
                    snapshot.DataValues[assignee.FieldKey!] = await RequireBoundPersonAsync(appCode, externalId, cancellationToken);
                }
            }
            else if (assignee.Source == FlowAssigneeSources.DataDepartmentHead)
            {
                var externalId = ReadField(data, assignee.FieldKey);
                if (externalId is not null)
                {
                    snapshot.DataValues[assignee.FieldKey!] = await organization.ResolveBoundDepartmentAsync(appCode,
                        externalId, cancellationToken)
                        ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                            $"部门 {externalId} 未在当前接入应用绑定，请先维护部门绑定。");
                }
            }
            else if (assignee.Source == FlowAssigneeSources.Matrix)
            {
                await AddMatrixMatchAsync(snapshot, assignee.Value ?? string.Empty, initiatorId, data,
                    cancellationToken);
            }
            else if (assignee.Source == FlowAssigneeSources.BusinessApi)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "该流程使用业务系统取人接口；取人钩子协议确认并配置前暂不能发起。");
            }
        }

        if (graph.Nodes.Any(node => node.Type == FlowNodeTypes.Automatic))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "该流程含业务自动动作；动作钩子协议确认并配置前暂不能发起。");
        }

        return BuildEngineContext(definition, graph, snapshot, initiator, data, now);
    }

    /// <summary>
    /// 后续节点以最新组织和矩阵重算路由，复用发起时保存的部门、人员选择和审批数据。
    /// </summary>
    /// <param name="instance">当前审批单。</param>
    /// <param name="definition">锁定的流程定义。</param>
    /// <param name="graph">锁定的执行图。</param>
    /// <param name="now">本次操作时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已更新组织与矩阵的纯引擎输入。</returns>
    public async Task<EngineContext> BuildContinuationAsync(InstanceEntity instance, FlowDefinition definition,
        FlowExecutionGraph graph, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var snapshot = await organization.LoadAssigneeSnapshotAsync(instance.InitiatorId, cancellationToken);
        var initiator = snapshot.People.SingleOrDefault(person => person.Id == instance.InitiatorId)
            ?? throw new UserFriendlyException(ErrorCodes.Conflict, "发起人已不在组织主数据中，无法继续审批。");
        snapshot.InitiatingDepartmentId = instance.InitiatorDepartmentId ?? string.Empty;
        var inputs = JsonSerializer.Deserialize<OpenRoutingInputs>(instance.RoutingInputsJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单发起路由快照为空。");
        snapshot.InitiatorSelections = inputs.InitiatorSelections;
        snapshot.DataValues = inputs.DataValues;
        using var dataDocument = JsonDocument.Parse(instance.ApprovalDataJson);
        var data = dataDocument.RootElement;
        foreach (var node in graph.Nodes)
        {
            if (node.Assignee?.Source == FlowAssigneeSources.Matrix)
            {
                await AddMatrixMatchAsync(snapshot, node.Assignee.Value ?? string.Empty,
                    instance.InitiatorId, data, cancellationToken);
            }
            else if (node.Assignee?.Source == FlowAssigneeSources.BusinessApi)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "该流程使用业务系统取人接口；取人钩子协议确认并配置前暂不能继续。");
            }
        }

        return BuildEngineContext(definition, graph, snapshot, initiator, data, now);
    }

    /// <summary>
    /// 重新提交新业务数据时重新核对找人字段的本应用绑定，保留发起时自选人员。
    /// </summary>
    /// <param name="appCode">审批单来源接入应用。</param>
    /// <param name="graph">锁定的执行图。</param>
    /// <param name="data">重新校验后的审批数据。</param>
    /// <param name="previousJson">发起时的路由输入。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>更新后的可信路由输入。</returns>
    public async Task<OpenRoutingInputs> RefreshRoutingInputsAsync(string appCode, FlowExecutionGraph graph,
        JsonElement data, string previousJson, CancellationToken cancellationToken = default)
    {
        var previous = JsonSerializer.Deserialize<OpenRoutingInputs>(previousJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单发起路由快照为空。");
        var result = new OpenRoutingInputs { InitiatorSelections = previous.InitiatorSelections };
        foreach (var node in graph.Nodes)
        {
            var assignee = node.Assignee;
            if (assignee?.Source == FlowAssigneeSources.DataPerson)
            {
                var externalId = ReadField(data, assignee.FieldKey);
                if (externalId is not null)
                {
                    result.DataValues[assignee.FieldKey!] = await RequireBoundPersonAsync(appCode, externalId,
                        cancellationToken);
                }
            }
            else if (assignee?.Source == FlowAssigneeSources.DataDepartmentHead)
            {
                var externalId = ReadField(data, assignee.FieldKey);
                if (externalId is not null)
                {
                    result.DataValues[assignee.FieldKey!] = await organization.ResolveBoundDepartmentAsync(appCode,
                        externalId, cancellationToken)
                        ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                            $"部门 {externalId} 未在当前接入应用绑定，请先维护部门绑定。");
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 将可信组织与数据快照中的条件统一计算成引擎输入。
    /// </summary>
    /// <param name="definition">锁定的流程定义。</param>
    /// <param name="graph">锁定的执行图。</param>
    /// <param name="snapshot">当前组织与发起选择。</param>
    /// <param name="initiator">发起人当前主数据。</param>
    /// <param name="data">已保存的审批数据。</param>
    /// <param name="now">操作时间。</param>
    /// <returns>引擎输入。</returns>
    private static EngineContext BuildEngineContext(FlowDefinition definition, FlowExecutionGraph graph,
        AssigneeSnapshot snapshot, AssigneePerson initiator, JsonElement data, DateTimeOffset now)
    {
        var expressionContext = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["initiator.department"] = snapshot.InitiatingDepartmentId,
            ["initiator.level"] = initiator.GradeRank,
        };
        var conditions = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(item => !item.IsDefault && !string.IsNullOrWhiteSpace(item.Condition)))
        {
            try
            {
                var evaluated = ApprovalExpression.Evaluate(edge.Condition!, data, expressionContext);
                conditions[edge.Id] = evaluated is bool selected ? selected :
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                        $"流程条件 {edge.Id} 的结果必须为真假值。");
            }
            catch (ApprovalExpressionException error)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"流程条件 {edge.Id} 计算失败：{error.Message}");
            }
        }

        return new EngineContext
        {
            Graph = graph,
            OccurredAt = now,
            Conditions = conditions,
            AssigneeSnapshot = snapshot,
            Operations = new OperationContext
            {
                InitiatorId = snapshot.InitiatorId,
                FlowCode = definition.Code,
                WithdrawBeforeFirstDecision = graph.WithdrawBeforeFirstDecision,
                ReturnMode = graph.ReturnMode,
            },
        };
    }

    /// <summary>
    /// 从可信配置查询矩阵命中，并校验结果只包含组织中在职人员。
    /// </summary>
    /// <param name="snapshot">当前组织快照。</param>
    /// <param name="matrixCode">矩阵编码。</param>
    /// <param name="initiatorId">内部发起人 ID。</param>
    /// <param name="data">业务数据快照。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    private async Task AddMatrixMatchAsync(AssigneeSnapshot snapshot, string matrixCode, string initiatorId,
        JsonElement data, CancellationToken cancellationToken)
    {
        var matches = await matrices.MatchAsync(matrixCode, initiatorId, data, cancellationToken);
        if (matches.Any(id => !snapshot.People.Any(person => person.Id == id && person.IsActive)))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                $"审批矩阵 {matrixCode} 返回了组织中不存在或已停用的人员。");
        }

        snapshot.MatrixMatches.Add(new AssigneeMatrixMatch
        {
            MatrixCode = matrixCode,
            AssigneeIds = matches.Distinct(StringComparer.Ordinal).ToList(),
        });
    }

    /// <summary>
    /// 按已发布流程限定发起部门和角色，部门配置由可信组织主数据解析。
    /// </summary>
    /// <param name="definition">流程定义。</param>
    /// <param name="initiator">发起人组织快照。</param>
    /// <param name="initiatingDepartmentId">经组织主数据验证的本次发起部门。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    private async Task CheckInitiatorScopeAsync(FlowDefinition definition, AssigneePerson initiator,
        string initiatingDepartmentId,
        CancellationToken cancellationToken)
    {
        if (definition.AllowedInitiatorRoles.Count > 0 &&
            !definition.AllowedInitiatorRoles.Intersect(initiator.Roles, StringComparer.Ordinal).Any())
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "发起人不在该流程允许的角色范围内。");
        }

        if (definition.AllowedInitiatorDepartments.Count == 0)
        {
            return;
        }

        // 流程范围由管理员配置，不能经接入应用的外部 ID 绑定再次解释。
        var master = await organizationRepository.LoadMasterDataAsync(cancellationToken);
        foreach (var configured in definition.AllowedInitiatorDepartments)
        {
            if (master.Departments.Any(department =>
                (department.Id == configured || department.Code == configured) &&
                department.Id == initiatingDepartmentId))
            {
                return;
            }
        }

        throw new UserFriendlyException(ErrorCodes.Forbidden, "发起人不在该流程允许的部门范围内。");
    }

    /// <summary>
    /// 将业务人员 ID 严格限定到当前应用既有绑定。
    /// </summary>
    /// <param name="appCode">当前接入应用编码。</param>
    /// <param name="externalId">业务系统人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>在职内部人员 ID。</returns>
    private async Task<string> RequireBoundPersonAsync(string appCode, string? externalId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "人员 ID 不能为空。");
        }

        return await organization.ResolveBoundPersonAsync(appCode, externalId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                $"人员 {externalId} 未在当前接入应用绑定或已经离职，请先维护人员绑定。");
    }

    /// <summary>
    /// 从已校验的审批数据读取用于找人的人员或部门字段。
    /// </summary>
    /// <param name="data">审批数据。</param>
    /// <param name="fieldKey">字段路径。</param>
    /// <returns>业务标识；字段未传时为空。</returns>
    private static string? ReadField(JsonElement data, string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批人来源缺少业务数据字段配置。");
        }

        var value = data;
        foreach (var segment in fieldKey.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                return null;
            }
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
