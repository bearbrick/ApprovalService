using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Domain.Center;

/// <summary>
/// 按锁定的流程版本与当前人员节点权限裁剪审批数据，供详情展示和搜索共用。
/// </summary>
public static class CenterFieldVisibility
{
    /// <summary>
    /// 只保留已定义且当前人员可见的字段与明细列。
    /// </summary>
    /// <param name="record">已完成参与关系授权的审批记录。</param>
    /// <param name="personId">可信登录人员 ID。</param>
    /// <param name="keys">需要投影的顶层字段。</param>
    /// <param name="fields">锁定版本的字段定义。</param>
    /// <param name="graph">锁定版本的执行图。</param>
    /// <returns>独立持有的安全 JSON 数据。</returns>
    public static JsonElement Filter(CenterRecord record, string personId, IEnumerable<string> keys,
        IReadOnlyList<FlowFieldDefinition> fields, FlowExecutionGraph graph)
    {
        var ownNodes = record.Tasks.Where(task => task.Status == EngineTaskStatuses.Pending &&
                (task.AssigneeId == personId || task.DelegatedForId == personId))
            .Select(task => task.NodeId).ToHashSet(StringComparer.Ordinal);
        // 历史参与人没有当前节点时采用全部节点隐藏集合；多节点权限冲突时隐藏优先。
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
    /// 仅匹配裁剪后字段的值，不匹配字段键与隐藏内容。
    /// </summary>
    /// <param name="visible">已裁剪的审批数据。</param>
    /// <param name="search">非空搜索词。</param>
    /// <returns>可见字段值命中时为真。</returns>
    public static bool ContainsValue(JsonElement visible, string search)
    {
        return visible.ValueKind switch
        {
            JsonValueKind.Object => visible.EnumerateObject().Any(property => ContainsValue(property.Value, search)),
            JsonValueKind.Array => visible.EnumerateArray().Any(item => ContainsValue(item, search)),
            JsonValueKind.String => visible.GetString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False =>
                visible.ToString().Contains(search, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>
    /// 逐层投影已定义字段，不透传业务系统夹带的额外键。
    /// </summary>
    /// <param name="source">当前层数据。</param>
    /// <param name="fields">当前层字段定义。</param>
    /// <param name="hidden">隐藏的完整字段路径。</param>
    /// <param name="selected">需要显示的完整字段路径。</param>
    /// <param name="prefix">当前字段路径前缀。</param>
    /// <param name="whole">父字段已被选中。</param>
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
}
