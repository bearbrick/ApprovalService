using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using ApprovalService.Engine.Expressions;

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
        var visibleSummary = CenterFieldVisibility.Filter(record, personId, definition.Data.SummaryFields, definition.Data.Fields, graph);
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
            IsUnread = record.Events.Any(item => item.Sequence > record.ReadThroughSequence &&
                item.Kind != "opinionDraftSaved" &&
                Read<EngineEvent>(item.PayloadJson).Audit?.ActorId != personId),
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
        var visibleData = CenterFieldVisibility.Filter(record, personId, definition.Data.Fields.Select(field => field.Key), definition.Data.Fields, graph);
        var visibleFields = DescribeFields(visibleData, definition.Data.Fields);
        return new CenterDetailResponse
        {
            Instance = Summary(record, personId),
            Data = visibleData,
            FieldDefinitions = visibleFields,
            Layout = DescribeLayout(definition.DetailLayout, visibleData, visibleFields),
            Tasks = record.Tasks.Select(task => new CenterTaskResponse
            {
                Id = task.Id,
                NodeId = task.NodeId,
                NodeName = graph.Nodes.SingleOrDefault(node => node.Id == task.NodeId)?.Name ?? task.NodeId,
                AssigneeId = task.AssigneeId,
                DelegatedForId = task.DelegatedForId,
                Status = task.Status,
                Opinion = task.Opinion,
                SignatureFileId = task.SignatureFileId,
                DueAt = task.DueAt,
                CompletedAt = task.CompletedAt,
                AllowedActions = TaskActionAvailability.GetAllowedActions(state, graph, task.Id, personId),
                SignatureRequired = graph.Nodes.SingleOrDefault(node => node.Id == task.NodeId)?.Approval?.SignatureRequired == true,
            }).ToArray(),
            // 暂存意见仍是个人草稿，不能进入其他参与者可见的时间线。
            Events = record.Events.Where(item => item.Kind != "opinionDraftSaved").Select(ProjectEvent).ToArray(),
            ActiveNodeIds = state.Tokens.Select(token => token.NodeId).Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>
    /// 仅投影授权后实际返回的字段和明细列，防止排版结构泄露隐藏定义。
    /// </summary>
    /// <param name="layout">本审批单锁定发布版本的排版。</param>
    /// <param name="visible">已按人员权限裁剪的数据。</param>
    /// <param name="fields">仅包含可见字段的展示定义。</param>
    /// <returns>安全详情排版；无可用自定义分组时回退自动排版。</returns>
    private static CenterDetailLayoutResponse DescribeLayout(FlowDetailLayout layout, JsonElement visible,
        IReadOnlyList<CenterFieldDefinitionResponse> fields)
    {
        if (layout.Mode == "embed" && !string.IsNullOrWhiteSpace(layout.EmbedPageCode))
        {
            return new CenterDetailLayoutResponse { Mode = "embed", EmbedPageCode = layout.EmbedPageCode };
        }
        if (layout.Mode != "custom")
        {
            return new CenterDetailLayoutResponse();
        }
        var known = fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var groups = new List<CenterLayoutGroupResponse>();
        foreach (var group in layout.Groups)
        {
            var columns = Math.Clamp(group.Columns, 1, 3);
            var items = new List<CenterLayoutItemResponse>();
            foreach (var source in group.Items)
            {
                if (!known.TryGetValue(source.FieldKey, out var field))
                {
                    continue;
                }
                var children = field.Children.Select(child => child.Key).ToHashSet(StringComparer.Ordinal);
                var widths = source.DetailColumnWidths.Where(pair => children.Contains(pair.Key) && pair.Value is > 0 and <= 100)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                string? uppercase = null;
                if (source.ShowMoneyUppercase && field.Type == FlowFieldTypes.Money &&
                    visible.TryGetProperty(field.Key, out var value) && value.ValueKind == JsonValueKind.Number &&
                    value.TryGetDecimal(out var amount) &&
                    amount is > -10000000000000000m and < 10000000000000000m)
                {
                    uppercase = RmbUppercase.Format(amount);
                }
                items.Add(new CenterLayoutItemResponse
                {
                    FieldKey = field.Key,
                    ColumnSpan = Math.Clamp(source.ColumnSpan, 1, columns),
                    DetailColumnWidths = widths,
                    MoneyUppercase = uppercase,
                });
            }
            if (items.Count == 0)
            {
                continue;
            }
            // 分组中存在隐藏字段时不返回原标题，避免标题暗示隐藏字段内容。
            var allVisible = items.Count == group.Items.Count;
            groups.Add(new CenterLayoutGroupResponse
            {
                Title = allVisible ? group.Title : "审批数据",
                Columns = columns,
                Items = items,
            });
        }
        return groups.Count == 0 ? new CenterDetailLayoutResponse() : new CenterDetailLayoutResponse
        {
            Mode = "custom",
            Groups = groups,
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
    /// 只从已返回的附件字段读取文件 ID，隐藏字段与未定义键不会参与元数据查询。
    /// </summary>
    /// <param name="detail">已授权且已裁剪的详情响应。</param>
    /// <returns>按首次出现顺序去重的文件 ID。</returns>
    public static IReadOnlyList<string> VisibleAttachmentIds(CenterDetailResponse detail)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectAttachments(detail.Data, detail.FieldDefinitions, ids, seen);
        return ids;
    }

    /// <summary>
    /// 按可见字段元数据递归读取顶层及明细行中的附件。
    /// </summary>
    /// <param name="container">本层已裁剪数据。</param>
    /// <param name="definitions">本层已裁剪字段定义。</param>
    /// <param name="ids">结果集合。</param>
    /// <param name="seen">跨字段去重集合。</param>
    private static void CollectAttachments(JsonElement container,
        IReadOnlyList<CenterFieldDefinitionResponse> definitions, List<string> ids, HashSet<string> seen)
    {
        if (container.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        foreach (var definition in definitions)
        {
            if (!container.TryGetProperty(definition.Key, out var value))
            {
                continue;
            }
            if (definition.Type == FlowFieldTypes.Attachment)
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    AddAttachmentId(value.GetString(), ids, seen);
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            AddAttachmentId(item.GetString(), ids, seen);
                        }
                    }
                }
            }
            else if (definition.Type == FlowFieldTypes.DetailTable && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in value.EnumerateArray())
                {
                    CollectAttachments(row, definition.Children, ids, seen);
                }
            }
        }
    }

    /// <summary>
    /// 过滤空值并保留文件在审批数据中首次出现的顺序。
    /// </summary>
    /// <param name="id">审批数据里的文件 ID。</param>
    /// <param name="ids">结果集合。</param>
    /// <param name="seen">去重集合。</param>
    private static void AddAttachmentId(string? id, List<string> ids, HashSet<string> seen)
    {
        if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
        {
            ids.Add(id);
        }
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
            SignatureFileId = payload.Audit?.SignatureFileId,
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
