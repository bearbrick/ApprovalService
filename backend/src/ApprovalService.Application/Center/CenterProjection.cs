using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Center;

/// <summary>
/// 将持久快照投影为审批中心响应；不直接输出运行状态或审计正文。
/// </summary>
internal static class CenterProjection
{
    /// <summary>
    /// 返回单据摘要和当前人员的待办标识。
    /// </summary>
    /// <param name="record">已授权记录。</param>
    /// <param name="personId">当前人员。</param>
    /// <returns>安全摘要。</returns>
    public static CenterInstanceResponse Summary(CenterRecord record, string personId)
    {
        var definition = Read<FlowDefinition>(record.FlowVersion.DefinitionJson);
        var graph = Read<FlowExecutionGraph>(record.FlowVersion.GraphJson);
        var instance = record.Instance;
        var state = Read<EngineState>(instance.StateJson);
        var visibleSummary = VisibleData(record, personId, definition.Data.SummaryFields, definition.Data.Fields, graph);
        var currentNodeIds = record.Tasks.Where(task => task.Status == EngineTaskStatuses.Pending)
            .Select(task => task.NodeId).Distinct(StringComparer.Ordinal).ToArray();
        var currentStep = currentNodeIds.Length switch
        {
            0 => null,
            1 => graph.Nodes.Where(node => node.Id == currentNodeIds[0]).Select(node => node.Name)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? currentNodeIds[0],
            _ => $"{currentNodeIds.Length} 个并行环节",
        };
        var bulkTaskIds = record.Tasks.Where(task => task.Status == EngineTaskStatuses.Pending &&
            (task.AssigneeId == personId || task.DelegatedForId == personId)).Where(task =>
            TaskActionAvailability.GetAllowedActions(state, graph, task.Id, personId)
                .Contains(OperationKinds.BulkApprove, StringComparer.Ordinal)).Select(task => task.Id).ToArray();
        return new CenterInstanceResponse
        {
            Id = instance.Id,
            SerialNo = instance.SerialNo,
            FlowCode = instance.FlowCode,
            FlowName = record.FlowName,
            SourceName = record.SourceName,
            InitiatorName = record.InitiatorName,
            CurrentStep = currentStep,
            BusinessKey = instance.BusinessKey,
            Title = instance.Title,
            Status = instance.Status,
            ClientAppCode = instance.ClientAppCode,
            InitiatorId = instance.InitiatorId,
            SubmittedById = instance.SubmittedById,
            IsUrgent = instance.IsUrgent,
            StartedAt = instance.StartedAt,
            EndedAt = instance.EndedAt,
            Summary = visibleSummary,
            SummaryFieldDefinitions = DescribeFields(visibleSummary, definition.Data.Fields),
            PendingTaskIds = record.Tasks.Where(task => task.Status == EngineTaskStatuses.Pending &&
                (task.AssigneeId == personId || task.DelegatedForId == personId)).Select(task => task.Id).ToArray(),
            CanBulkApprove = bulkTaskIds.Length > 0,
            BulkApprovableTaskIds = bulkTaskIds,
        };
    }

    /// <summary>
    /// 返回只读详情和安全投影的进度与时间线。
    /// </summary>
    /// <param name="record">已授权详情快照。</param>
    /// <param name="personId">当前人员。</param>
    /// <returns>安全详情。</returns>
    public static CenterDetailResponse Detail(CenterRecord record, string personId)
    {
        var graph = Read<FlowExecutionGraph>(record.FlowVersion.GraphJson);
        var definition = Read<FlowDefinition>(record.FlowVersion.DefinitionJson);
        var state = Read<EngineState>(record.Instance.StateJson);
        var visibleData = VisibleData(record, personId, definition.Data.Fields.Select(field => field.Key), definition.Data.Fields, graph);
        return new CenterDetailResponse
        {
            Instance = Summary(record, personId),
            Data = visibleData,
            FieldDefinitions = DescribeFields(visibleData, definition.Data.Fields),
            Tasks = record.Tasks.Select(task => new CenterTaskResponse
            {
                Id = task.Id,
                NodeId = task.NodeId,
                NodeName = graph.Nodes.SingleOrDefault(node => node.Id == task.NodeId)?.Name ?? task.NodeId,
                AssigneeId = task.AssigneeId,
                DelegatedForId = task.DelegatedForId,
                Status = task.Status,
                Opinion = task.Opinion,
                DueAt = task.DueAt,
                CompletedAt = task.CompletedAt,
                AllowedActions = TaskActionAvailability.GetAllowedActions(state, graph, task.Id, personId),
            }).ToArray(),
            // 暂存意见仍是个人草稿，不能进入其他参与者可见的时间线。
            Events = record.Events.Where(item => item.Kind != "opinionDraftSaved").Select(ProjectEvent).ToArray(),
            ActiveNodeIds = state.Tokens.Select(token => token.NodeId).Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>
    /// 只为已实际返回的字段创建展示定义，明细列同样按已裁剪数据求交集。
    /// </summary>
    /// <param name="visible">已按人员和节点权限过滤的数据。</param>
    /// <param name="definitions">本审批单锁定发布版本的字段定义。</param>
    /// <returns>不暴露隐藏字段名称的展示元数据。</returns>
    private static IReadOnlyList<CenterFieldDefinitionResponse> DescribeFields(JsonElement visible,
        IReadOnlyList<FlowFieldDefinition> definitions)
    {
        if (visible.ValueKind != JsonValueKind.Object)
        {
            return [];
        }
        var result = new List<CenterFieldDefinitionResponse>();
        foreach (var definition in definitions)
        {
            if (!visible.TryGetProperty(definition.Key, out var value))
            {
                continue;
            }
            var children = new List<CenterFieldDefinitionResponse>();
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in definition.Children)
                {
                    // 隐藏列已在每一行剔除；空表不虚构列的可见性。
                    if (value.EnumerateArray().Any(row => row.ValueKind == JsonValueKind.Object &&
                        row.TryGetProperty(child.Key, out _)))
                    {
                        children.Add(new CenterFieldDefinitionResponse
                        {
                            Key = child.Key,
                            Name = child.Name,
                            Type = child.Type,
                        });
                    }
                }
            }
            result.Add(new CenterFieldDefinitionResponse
            {
                Key = definition.Key,
                Name = definition.Name,
                Type = definition.Type,
                Children = children,
            });
        }
        return result;
    }

    /// <summary>
    /// 按当前人员待办节点的隐藏规则过滤字段，不让摘要绕过详情权限。
    /// </summary>
    /// <param name="record">已授权审批记录。</param>
    /// <param name="personId">当前人员。</param>
    /// <param name="keys">本次允许投影的字段。</param>
    /// <param name="fields">发布时的字段定义，含明细列。</param>
    /// <param name="graph">固定版本执行图。</param>
    /// <returns>独立持有的 JSON 数据。</returns>
    private static JsonElement VisibleData(CenterRecord record, string personId, IEnumerable<string> keys,
        IReadOnlyList<FlowFieldDefinition> fields, FlowExecutionGraph graph)
    {
        var ownNodes = record.Tasks.Where(task => task.Status == EngineTaskStatuses.Pending &&
                (task.AssigneeId == personId || task.DelegatedForId == personId))
            .Select(task => task.NodeId).ToHashSet(StringComparer.Ordinal);
        // 未携带节点上下文的历史参与人采用全部节点隐藏集合；多节点权限冲突时隐藏优先。
        // 发起人与代发人仍只读取发布定义中的字段，不返回额外业务数据。
        var sourceOwner = record.Instance.InitiatorId == personId || record.Instance.SubmittedById == personId;
        var hidden = sourceOwner ? new HashSet<string>(StringComparer.Ordinal) : graph.Nodes
            .Where(node => ownNodes.Count == 0 || ownNodes.Contains(node.Id))
            .SelectMany(node => node.FieldPermissions).Where(permission => permission.Access == FlowFieldAccess.Hidden)
            .Select(permission => permission.FieldKey).ToHashSet(StringComparer.Ordinal);
        using var data = JsonDocument.Parse(record.Instance.ApprovalDataJson);
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        var visible = FilterFields(data.RootElement, fields, hidden, selected, string.Empty, false);
        return JsonSerializer.SerializeToElement(visible, ApprovalJson.Options);
    }

    /// <summary>
    /// 逐层投影已定义字段，隐藏明细列且不透传业务系统额外夹带的键。
    /// </summary>
    /// <param name="source">当前层级的对象。</param>
    /// <param name="fields">当前层级字段定义。</param>
    /// <param name="hidden">完整隐藏路径集合。</param>
    /// <param name="selected">需要展示的完整路径集合。</param>
    /// <param name="prefix">当前路径前缀。</param>
    /// <param name="whole">父字段已被选中时读取其余可见子字段。</param>
    /// <returns>安全的字段对象。</returns>
    private static Dictionary<string, object> FilterFields(JsonElement source,
        IReadOnlyList<FlowFieldDefinition> fields, HashSet<string> hidden, HashSet<string> selected,
        string prefix, bool whole)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var path = prefix + field.Key;
            var include = whole || selected.Contains(path);
            if (hidden.Contains(path) || (!include && !selected.Any(key => key.StartsWith(path + ".", StringComparison.Ordinal))) ||
                source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(field.Key, out var value))
            {
                continue;
            }
            if (field.Children.Count > 0 && value.ValueKind == JsonValueKind.Array)
            {
                result[field.Key] = value.EnumerateArray().Select(row => FilterFields(row, field.Children,
                    hidden, selected, path + ".", include)).ToArray();
            }
            else if (field.Children.Count == 0 && include)
            {
                result[field.Key] = value.Clone();
            }
        }
        return result;
    }

    /// <summary>
    /// 仅提取对审批人展示的事件信息，隔离字段值和其他内部载荷。
    /// </summary>
    /// <param name="item">持久审计事件。</param>
    /// <returns>只读时间线项。</returns>
    private static CenterEventResponse ProjectEvent(OperationEventEntity item)
    {
        var payload = Read<EngineEvent>(item.PayloadJson);
        return new CenterEventResponse
        {
            EventId = item.Id,
            Sequence = item.Sequence,
            Kind = item.Kind,
            NodeId = payload.NodeId,
            ActorId = payload.Audit?.ActorId,
            OnBehalfOfId = payload.Audit?.OnBehalfOfId,
            Opinion = payload.Audit?.Opinion,
            OccurredAt = item.OccurredAt,
        };
    }

    /// <summary>
    /// 读取已持久化的规范 JSON，损坏时作为服务器错误暴露。
    /// </summary>
    /// <typeparam name="T">目标快照类型。</typeparam>
    /// <param name="json">规范 JSON。</param>
    /// <returns>非空快照。</returns>
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, ApprovalJson.Options)
        ?? throw new InvalidOperationException("审批快照内容为空。");
}
